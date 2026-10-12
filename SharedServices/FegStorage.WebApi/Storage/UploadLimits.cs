// Author: Md. Sakibur Rahman
// Date:   2026-05-14
namespace FegStorage.WebApi.Storage;

// Bound from the root MaxUploadBytes config key. Defaults to 50 MB.
public sealed class UploadLimits
{
    public long MaxUploadBytes { get; set; } = 52_428_800;
}
