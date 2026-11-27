"""The CI shutdown debugger must preserve clean exits and fatal signals."""
from pathlib import Path
import os
import signal
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from dolphin_debug import trace_host_shutdown

class ShutdownTraceChecks(unittest.TestCase):
    def scenario(self,body,expected,abort=False):
        with tempfile.TemporaryDirectory() as directory:
            trace=Path(directory)/'shutdown.log'
            code=("import os,signal,sys; signal.signal(signal.SIGTERM,lambda *_: "+body+"); "
                  "print('ready',flush=True); signal.pause()")
            process=subprocess.Popen([sys.executable,'-c',code],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
            try:
                self.assertEqual(process.stdout.readline().strip(),'ready')
                with trace_host_shutdown(process,trace):
                    process.terminate()
                    process.wait(timeout=15)
                self.assertEqual(process.returncode,expected)
                text=trace.read_text()
                self.assertIn('ANIMALTOWN HOST SHUTDOWN TRACE READY',text)
                self.assertNotIn('TRACE ERROR',text)
                if expected<0:
                    self.assertIn('ANIMALTOWN HOST SHUTDOWN THREAD',text)
                    if abort:self.assertIn('abort',text.lower())
            finally:
                if process.poll() is None:process.kill();process.wait()
                process.stdout.close();process.stderr.close()
    def test_normal_exit_stays_zero(self):self.scenario('sys.exit(0)',0)
    def test_abort_keeps_original_signal(self):self.scenario('os.abort()',-signal.SIGABRT,abort=True)
    def test_direct_abort_signal_is_delivered(self):self.scenario('os.kill(os.getpid(),signal.SIGABRT)',-signal.SIGABRT)
    def test_nonzero_exit_stays_nonzero(self):self.scenario('sys.exit(7)',7)

if __name__=='__main__':unittest.main()
