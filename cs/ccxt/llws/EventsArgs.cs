using System.Runtime.CompilerServices;

namespace ccxt;

public class JsonMessageEventArgs : EventArgs
{
    public string Message { get; set; }
    
    public JsonMessageEventArgs(string message)
    {
        Message = message;
    }
}

public class ByteArrayMessageEventArgs : EventArgs
{
    public byte[] ByteArray { get; set; }
    
    public ByteArrayMessageEventArgs(byte[] byteArray)
    {
        ByteArray = byteArray;
    }
}

