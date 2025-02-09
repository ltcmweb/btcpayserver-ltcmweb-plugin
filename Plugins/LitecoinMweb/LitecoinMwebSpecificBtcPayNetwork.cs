namespace BTCPayServer.Plugins.LitecoinMweb;

public class LitecoinMwebSpecificBtcPayNetwork : BTCPayNetworkBase
{
    public int MaxTrackedConfirmation = 10;
    public string UriScheme { get; set; }
}
