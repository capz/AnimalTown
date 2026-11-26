internal sealed class OnlineDeviceLink
{
    internal readonly string Origin, Town, AccountId, DeviceId, AccessToken;
    internal readonly long AccessExpiresAt;
    internal readonly int Generation;

    internal OnlineDeviceLink(string origin,string town,string accountId,string deviceId,string access,long expiresAt,int generation)
    {
        Origin=origin;Town=town;AccountId=accountId;DeviceId=deviceId;
        AccessToken=access;AccessExpiresAt=expiresAt;Generation=generation;
    }
}
