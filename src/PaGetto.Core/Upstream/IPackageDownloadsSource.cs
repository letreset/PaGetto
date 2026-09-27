using System.Collections.Generic;
using System.Threading.Tasks;

namespace PaGetto.Core.Upstream;

public interface IPackageDownloadsSource
{
    Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync();
}
