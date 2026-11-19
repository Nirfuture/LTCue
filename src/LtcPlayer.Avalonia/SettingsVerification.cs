using System.Text.Json;
using System.Text.Json.Nodes;
using LtcPlayer.App;

namespace LtcPlayer.Avalonia;

internal static class SettingsVerification
{
    // Records containing arrays compare array references. Persistence checks must
    // compare every serialized setting, including each resolution preset value.
    internal static bool Equivalent(InputSettings expected, InputSettings actual) =>
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected), JsonSerializer.SerializeToNode(actual));
}
