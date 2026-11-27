"""Check the emulator DNS contract and reject unusable probe replies offline."""
from pathlib import Path
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
        with patch('dolphin_network.socket.socket', return_value=udp), patch('dolphin_network.secrets.randbits', return_value=42):
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


unittest.main()
