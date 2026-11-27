set pagination off
set confirm off
set print frame-arguments none
set print entry-values no
set print thread-events off
handle SIGTERM nostop noprint pass
handle SIGPIPE nostop noprint pass
handle SIGSEGV nostop noprint pass
handle SIGABRT stop print pass
python
try:
    if not gdb.selected_inferior().pid:
        raise gdb.GdbError("Shutdown debugger did not attach")
    print("ANIMALTOWN HOST SHUTDOWN TRACE READY", flush=True)
    gdb.execute("continue")
    inferior = gdb.selected_inferior()
    if inferior.pid:
        stopped = gdb.selected_thread()
        print("ANIMALTOWN HOST SHUTDOWN STOPPED THREAD " + str(stopped.num), flush=True)
        gdb.execute("bt 12")
        for thread in inferior.threads()[:64]:
            thread.switch()
            print("ANIMALTOWN HOST SHUTDOWN THREAD " + str(thread.num), flush=True)
            gdb.execute("bt 12")
        stopped.switch()
        # Deliver the original stop signal; an abort must still fail the test.
        gdb.execute("continue")
except gdb.error as error:
    print("ANIMALTOWN HOST SHUTDOWN TRACE ERROR " + type(error).__name__, flush=True)
end
