namespace AnimalTown.Online;

// One control request may wait for at most two one-minute request windows.
// The caller retains its exact operation ID, page offset and bytes on retry.
public struct OnlineHttpRetry
{
    int retries;
    public bool TryWait(int status,string? retryAfter,out int milliseconds)
    {
        milliseconds=0;
        if(status!=429||retries>=2||retryAfter==null||retryAfter.Length<1||retryAfter.Length>2)return false;
        int seconds=0;
        for(int i=0;i<retryAfter.Length;i++)
        {
            char digit=retryAfter[i];if(digit<'0'||digit>'9')return false;
            seconds=seconds*10+digit-'0';
        }
        if(seconds<1||seconds>60)return false;
        retries++;milliseconds=seconds*1000;return true;
    }
}
