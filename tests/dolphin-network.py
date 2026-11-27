"""Check the emulator DNS contract and reject unusable probe replies offline."""
from pathlib import Path
from contextlib import redirect_stdout
from io import StringIO
import importlib.util
import json
import os
import tempfile
from types import SimpleNamespace
import struct
import sys
import unittest
from unittest.mock import MagicMock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from dolphin_network import DNS_SERVER, online_core_settings, probe


class DolphinNetworkChecks(unittest.TestCase):
    def test_resolver_matches_console_sdk_default(self):
        self.assertEqual(DNS_SERVER, '8.8.8.8')
        self.assertIn(f'BBA_BUILTIN_DNS = {DNS_SERVER}', online_core_settings())
        self.assertIn('SerialPort1 = 12', online_core_settings())

    def run_probe(self, reply):
        udp = MagicMock()
        udp.__enter__.return_value = udp
        udp.recv.return_value = reply
        with patch('dolphin_network.socket.socket', return_value=udp), patch('dolphin_network.secrets.randbits', return_value=42), redirect_stdout(StringIO()):
            probe('online.example', timeout=3)
        return udp

    def test_uses_exact_resolver_and_bounded_query(self):
        udp = self.run_probe(struct.pack('!6H', 42, 0x8180, 1, 2, 0, 0))
        udp.connect.assert_called_once_with((DNS_SERVER, 53))
        udp.settimeout.assert_called_once_with(3)
        self.assertEqual(udp.send.call_args.args[0], struct.pack('!6H', 42, 0x0100, 1, 0, 0, 0) + b'\x06online\x07example\0\0\1\0\1')

    def test_accepts_authoritative_answers(self):
        self.run_probe(struct.pack('!6H', 42, 0x8580, 1, 1, 0, 0))

    def test_rejects_incomplete_mismatched_negative_and_truncated_answers(self):
        for reply in (b'', struct.pack('!6H', 41, 0x8180, 1, 1, 0, 0),
                      struct.pack('!6H', 42, 0x8183, 1, 0, 0, 0),
                      struct.pack('!6H', 42, 0x8380, 1, 1, 0, 0),
                      struct.pack('!6H', 42, 0x0100, 1, 1, 0, 0),
                      struct.pack('!6H', 42, 0x8180, 1, 0, 0, 0)):
            with self.subTest(reply=reply), self.assertRaises(RuntimeError):
                self.run_probe(reply)

    def test_invalid_names_do_not_open_a_socket(self):
        with patch('dolphin_network.socket.socket') as opening:
            for host in ('', 'a..example', 'https://example', 'a' * 64 + '.example', 'é.example'):
                with self.subTest(host=host), self.assertRaises(ValueError):
                    probe(host)
            opening.assert_not_called()



class OnlineFailureCaptureChecks(unittest.TestCase):
    def test_native_errors_stop_before_automatic_confirmation(self):
        driver_path = Path(__file__).resolve().parents[1] / 'tools' / 'online-play-smoke.py'
        spec = importlib.util.spec_from_file_location('online_play_driver', driver_path)
        driver = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(driver)
        for phase in ('LINK', 'FLOW'):
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                pressed, captured = [], []
                reader, writer = os.pipe()
                context = {
                    'root': root, 'user': root / 'user',
                    '__file__': str(driver_path.with_name('iso-smoke.py')),
                    'controller': writer,
                    'p': SimpleNamespace(poll=lambda: None),
                    'check_runtime_failure': lambda: None,
                    'runtime_log_text': lambda: 'ANIMALTOWN ONLINE ' + phase + ' FAILED io',
                    'screenshot': captured.append,
                    'press_presented': lambda button, clock: pressed.append(button),
                    'wait_profiles': lambda: None,
                    'committed_card_sequence': lambda path: 7,
                }
                try:
                    clock_module = SimpleNamespace(AviGuestClock=lambda *args, **kwargs: object())
                    with patch.object(sys, 'argv', ['iso-smoke.py', 'disc', directory, '--online-play-fixture', directory]), patch.object(sys, 'path', list(sys.path)), patch.dict(sys.modules, {'avi_guest_clock': clock_module}), redirect_stdout(StringIO()):
                        with self.assertRaisesRegex(AssertionError, 'Native online operation failed'):
                            driver.run(context)
                    self.assertEqual(pressed, ['A', 'A'])
                    self.assertEqual(captured, ['online-play-' + phase.lower() + '-failure.png'])
                    self.assertFalse(json.loads((root / 'online-play-report.json').read_text())['passed'])
                    self.assertEqual(os.read(reader, 64), b'SET MAIN 0.5 0.5\n')
                finally:
                    os.close(reader)
                    os.close(writer)



    def run_return_failure(self, repeated):
        driver_path = Path(__file__).resolve().parents[1] / 'tools' / 'online-play-smoke.py'
        spec = importlib.util.spec_from_file_location('online_return_driver', driver_path)
        driver = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(driver)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pressed, captured = [], []
            reader, writer = os.pipe()
            reads = 0
            def log():
                nonlocal reads
                reads += 1
                if repeated:
                    return ('ANIMALTOWN ONLINE RETURN FAILED SocketException\n' *
                            max(1, len(pressed) - 1))
                # Keep observing the same failure before a separate fatal
                # error ends the fixture. It must trigger just one retry.
                return ('ANIMALTOWN ONLINE RETURN FAILED SocketException\n' +
                        ('ANIMALTOWN ONLINE FLOW FAILED io' if reads > 7 else ''))
            context = {
                'root': root, 'user': root / 'user',
                '__file__': str(driver_path.with_name('iso-smoke.py')),
                'controller': writer,
                'p': SimpleNamespace(poll=lambda: None),
                'check_runtime_failure': lambda: None,
                'runtime_log_text': log,
                'screenshot': captured.append,
                'press_presented': lambda button, clock: pressed.append(button),
                'wait_profiles': lambda: None,
                'committed_card_sequence': lambda path: 7,
            }
            try:
                clock_module = SimpleNamespace(AviGuestClock=lambda *args, **kwargs: object())
                with patch.object(sys, 'argv', ['iso-smoke.py', 'disc', directory, '--online-play-fixture', directory]), patch.object(sys, 'path', list(sys.path)), patch.dict(sys.modules, {'avi_guest_clock': clock_module}), patch.object(driver.time, 'sleep', lambda seconds: None), redirect_stdout(StringIO()):
                    with self.assertRaisesRegex(AssertionError, 'Native online operation failed'):
                        driver.run(context)
                report = json.loads((root / 'online-play-report.json').read_text())
                self.assertFalse(report['passed'])
                self.assertEqual(os.read(reader, 64), b'SET MAIN 0.5 0.5\n')
                return pressed, captured, report
            finally:
                os.close(reader)
                os.close(writer)

    def test_return_recovery_is_bounded_and_preserves_failure_report(self):
        pressed, captured, report = self.run_return_failure(repeated=True)
        self.assertEqual(pressed, ['A', 'A', 'A', 'A'])
        self.assertEqual(report['return_retries'], 2)
        self.assertEqual(captured, ['online-play-return-retry-1.png',
                                   'online-play-return-retry-2.png',
                                   'online-play-return-failure.png'])

    def test_repeated_observation_of_one_return_error_retries_once(self):
        pressed, captured, report = self.run_return_failure(repeated=False)
        self.assertEqual(pressed, ['A', 'A', 'A'])
        self.assertEqual(report['return_retries'], 1)
        self.assertEqual(captured, ['online-play-return-retry-1.png',
                                   'online-play-flow-failure.png'])


unittest.main()
