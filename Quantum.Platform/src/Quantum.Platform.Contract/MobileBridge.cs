using NOF.Contract;
using Quantum.Mobile.Internal;
using System.Text.Json;

namespace Quantum.Platform.Contract;

[TransportOverHttp(HttpRpcStyle.JsonRpc, "/rpc/mobile")]
public interface IMobileBridgeService : IRpcService
{
    Result<JsonElement?> ExchangeMobileBridge(BridgeRequest request);
}
