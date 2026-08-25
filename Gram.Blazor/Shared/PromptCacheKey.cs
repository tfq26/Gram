using System.Security.Cryptography;
using System.Text;

namespace Gram.Blazor.Shared;

public static class PromptCacheKey
{
    public static string ForAgent(string modelId, string workspacePath)
    {
        var material = $"gram-agent:v1\n{modelId}\n{Path.GetFullPath(workspacePath)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"gram-agent-v1:{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }
}
