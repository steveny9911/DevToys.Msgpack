using DevToys.Api;
using System.ComponentModel.Composition;

namespace DevToys.Msgpack;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(DevToysMsgpackResourceAssemblyIdentifier))]
internal sealed class DevToysMsgpackResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
    {
        return new ValueTask<FontDefinition[]>([]);
    }
}
