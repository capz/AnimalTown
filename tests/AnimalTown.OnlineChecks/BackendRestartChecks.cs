static class BackendRestartChecks
{
    internal static async Task<bool> Restart(Action<bool,string> check)
    {
        string? request=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_RESTART_REQUEST"),ack=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_RESTART_ACK");
        if(string.IsNullOrEmpty(request)||string.IsNullOrEmpty(ack))return false;
        string nonce=Guid.NewGuid().ToString("N");File.WriteAllText(request+".tmp",nonce);File.Move(request+".tmp",request);
        long deadline=Environment.TickCount64+20000;
        while(Environment.TickCount64<deadline)
        {
            if(File.Exists(ack)&&File.ReadAllText(ack)==nonce){check(true,"The test harness restarted the actual backend against the same database");return true;}
            await Task.Delay(100);
        }
        throw new Exception("Backend restart was not acknowledged by the owning test harness");
    }
}
