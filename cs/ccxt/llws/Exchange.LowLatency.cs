using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace ccxt;

public partial class Exchange
{

    private bool isJsonSubscribed = false;
    private bool isByteArraySubscribed = false; 
    protected List<string> subscriptions = new List<string>();
    public event HandleJsonStringMessageDelegate WatchOrderBookJsonMessageReceived;
    public event HandleByteArrayMessageDelegate WatchOrderBookByteArrayReceived;
    public event HandleJsonStringMessageDelegate WatchMyTradesJsonMessageReceived;
    public event HandleByteArrayMessageDelegate WatchMyTradesByteArrayReceived;
    public event HandleJsonStringMessageDelegate WatchOrdersJsonMessageReceived;
    public event HandleByteArrayMessageDelegate WatchOrdersByteArrayReceived;

    public virtual void OnWatchOrderBookJsonMessageReceived(object sender, JsonMessageEventArgs eventArgs) => WatchOrderBookJsonMessageReceived?.Invoke(sender, eventArgs);
    public virtual void OnWatchOrderBookByteArrayReceived(object sender, ByteArrayMessageEventArgs eventArgs) => WatchOrderBookByteArrayReceived?.Invoke(sender, eventArgs);
    public virtual void OnWatchMyTradesJsonMessageReceived(object sender, JsonMessageEventArgs eventArgs) => WatchMyTradesJsonMessageReceived?.Invoke(sender, eventArgs);
    public virtual void OnWatchMyTradesByteArrayReceived(object sender, ByteArrayMessageEventArgs eventArgs) => WatchMyTradesByteArrayReceived?.Invoke(sender, eventArgs);
    public virtual void OnWatchOrdersJsonMessageReceived(object sender, JsonMessageEventArgs eventArgs) => WatchOrdersJsonMessageReceived?.Invoke(sender, eventArgs);
    public virtual void OnWatchOrdersByteArrayReceived(object sender, ByteArrayMessageEventArgs eventArgs) => WatchOrdersByteArrayReceived?.Invoke(sender, eventArgs);
    
    protected virtual IEnumerable<WebSocketClient> GetWsClientsForSubscription(string subscriptionType, Dictionary<string, object> parameters = null){
        throw new NotImplementedException("Get Ws Client method not implemented for this exchange.");
    }

    protected virtual void ProcessJsonMessage(object sender, JsonMessageEventArgs eventArgs){
        // Implement for every exchange as needed
    }

    protected virtual void ProcessByteArrayMessage(object sender, ByteArrayMessageEventArgs eventArgs){
        // Implement for every exchange as needed
    }

    public virtual void SubscribeLLFeed(string subscriptionType){
        // passing market type is not smart solution it is temporary until solution that is fitting strucuture is found
        var clients = GetWsClientsForSubscription(subscriptionType);
        foreach(var client in clients){
            if(!subscriptions.Contains(subscriptionType))
                subscriptions.Add(subscriptionType);
            if(!isJsonSubscribed && subscriptionType.EndsWith("Json")){
                    
                client.jsonStringMessageReceived += this.ProcessJsonMessage;        
                isJsonSubscribed = true;
            }
            else if(!isByteArraySubscribed && subscriptionType.EndsWith("ByteArray")){
                throw new NotImplementedException("ByteArray message processing not implemented yet.");
                // isByteArraySubscribed = true;
                // if(client.byteArrayMessageReceived != null)
                    // client.byteArrayMessageReceived += ProcessJsonMessage;
            }
        }
    }

    public virtual void UnsubscribeLLFeed(string subscriptionType){
        var clients = GetWsClientsForSubscription(subscriptionType);
        foreach(var client in clients){
            if(subscriptions.Contains(subscriptionType))
                subscriptions.Remove(subscriptionType);
            if(isJsonSubscribed && subscriptionType.EndsWith("Json")){
                client.jsonStringMessageReceived -= ProcessJsonMessage;
                isJsonSubscribed = false;
            }
            else if(isByteArraySubscribed && subscriptionType.EndsWith("ByteArray")){
                throw new NotImplementedException("ByteArray message processing not implemented yet.");
                // isByteArraySubscribed = false;
                // client.byteArrayMessageReceived -= ProcessJsonMessage;
            }
        }
    }

    public virtual PreparedCreateOrderMessage CreateOrderPrepareMessageWs(Dictionary<string, object> parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual PreparedCancelOrderMessage CancelOrderPrepareMessageWs(Dictionary<string, object> parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual PreparedEditOrderMessage EditOrderPrepareMessageWs(Dictionary<string, object> parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual object PostProcessResponse(object response, string symbol){
        return response;
    }
    
    protected virtual Placeholder<string> GetParametersPlaceholder(object parameters){

        var parametersJsonString = (parameters != null && ((Dictionary<string, object>)parameters).Count() > 0) ? JsonConvert.SerializeObject(parameters) : null;
        var parametersPlaceHolder = new Placeholder<string>(parametersJsonString, parametersJsonString);
        return parametersPlaceHolder;
    }

    public virtual object GetRequestId(string url = null){
        throw new NotImplementedException("GetRequestId is implemented only under specific exchanges.");
    }

    public virtual string GetParametersPlaceholder(Dictionary<string, object> parameters){
        return JsonConvert.SerializeObject(parameters);
    }

    public virtual string SignParamsTemplateQuery(object parameters = null){
        throw new NotImplementedException("SignParamsTemplateQuery is implemented only under specific exchanges.");
    }

    public virtual async Task<byte[]> GetSignatureFromTemplateQuery(string query)
    {
        throw new NotImplementedException("GetSignatureTemplateQuery is implemented only under specific exchanges.");
    }
}