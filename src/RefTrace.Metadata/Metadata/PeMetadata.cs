using System.Collections.ObjectModel;
using System.Reflection.Metadata;

namespace RefTrace.Metadata;

internal static class PeMetadata
{
    public static IReadOnlyDictionary<string, string?> AssemblyProperties(MetadataReader reader)
    {
        var names = new MetadataNames(reader);
        SortedDictionary<string, string?> values = [with(StringComparer.Ordinal)];
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (names.Method(attribute.Constructor).Type != "System.Reflection.AssemblyMetadataAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid assembly metadata attribute prolog.");
            var key = blob.ReadSerializedString();
            var value = blob.ReadSerializedString();
            if (key is not null) values[key] = value;
        }
        return new ReadOnlyDictionary<string, string?>(values);
    }

    // C# 15's updated memory-safety rules record their version on the module (2 for SDK 11.0.100).
    public static int? MemorySafetyRulesVersion(MetadataReader reader)
    {
        var names = new MetadataNames(reader);
        foreach (var handle in reader.GetModuleDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (names.Method(attribute.Constructor).Type != "System.Runtime.CompilerServices.MemorySafetyRulesAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid memory-safety rules attribute prolog.");
            return blob.ReadInt32();
        }
        return null;
    }
}
