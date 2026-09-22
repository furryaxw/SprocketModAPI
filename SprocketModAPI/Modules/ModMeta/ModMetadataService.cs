using System;
using System.Collections.Generic;
using System.Text;

namespace SprocketModAPI
{
    // 只读元数据快照服务。快照按需重建；内容未变化时不触发 `Changed`。
    internal sealed class ModMetadataService : IModMetadataService, IDisposable
    {
        private readonly ILoadedModSource source;
        private IReadOnlyList<ModMetadata> entries = Array.Empty<ModMetadata>();
        private string signature = "";
        private bool initialized;
        private bool disposed;

        internal ModMetadataService(ILoadedModSource source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public IReadOnlyList<ModMetadata> Entries
        {
            get
            {
                if (disposed)
                    return Array.Empty<ModMetadata>();
                if (!initialized)
                    Refresh();
                return entries;
            }
        }

        public event Action? Changed;

        public ModMetadata? Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            foreach (ModMetadata entry in Entries)
            {
                if (string.Equals(entry.Id, id, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        public void Refresh()
        {
            if (disposed)
                return;

            var built = new List<ModMetadata>();
            foreach (LoadedModDescriptor descriptor in source.Read())
            {
                try
                {
                    built.Add(ModMetadataReader.Read(descriptor));
                }
                catch (Exception)
                {
                    // 单个模组的元数据合并失败不得影响其他模组；读取器已做降级，这里是最后一道隔离。
                }
            }

            built.Sort(static (left, right) =>
            {
                int byName = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
            });

            string builtSignature = BuildSignature(built);
            bool changed = !string.Equals(builtSignature, signature, StringComparison.Ordinal);
            entries = built;
            signature = builtSignature;
            initialized = true;

            if (changed)
                Changed?.Invoke();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            initialized = false;
            entries = Array.Empty<ModMetadata>();
            signature = "";
            Changed = null;
        }

        // 内容指纹：任何影响菜单展示的字段变化都要进入这里。
        private static string BuildSignature(List<ModMetadata> list)
        {
            var builder = new StringBuilder();
            foreach (ModMetadata entry in list)
            {
                builder.Append(entry.Id).Append('\u0001')
                    .Append(entry.DisplayName).Append('\u0001')
                    .Append(entry.Version).Append('\u0001')
                    .Append(entry.Location).Append('\u0001')
                    .Append((int)entry.Kind).Append('\u0001')
                    .Append(entry.IsDisabled ? '1' : '0').Append('\u0002');
            }

            return builder.ToString();
        }
    }
}
