using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SprocketModAPI
{
    internal interface IUiBackend : IDisposable
    {
        UiCapabilitySnapshot Capabilities { get; }
        UiCreateResult<IUiButtonHandle> CreateButton(string ownerId, UiButtonDefinition definition);
        UiCreateResult<IUiMenuButtonHandle> CreateMenuButton(string ownerId, UiMenuButtonDefinition definition);
        void SceneUnloaded();
    }

    internal sealed class UiScopeCore : IUiScope
    {
        private readonly string ownerId;
        private readonly IUiBackend backend;
        private readonly HashSet<IDisposable> handles = new();
        private bool disposed;

        internal UiScopeCore(string ownerId, IUiBackend backend)
        {
            this.ownerId = ownerId;
            this.backend = backend;
        }

        public Task<UiCreateResult<IUiButtonHandle>> CreateButtonAsync(
            UiButtonDefinition definition,
            CancellationToken cancellationToken = default)
        {
            if (disposed)
                return Task.FromResult(UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI scope is disposed."));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.Cancelled, "UI creation was cancelled."));
            if (definition == null)
                return Task.FromResult(UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.InvalidParent, "Button definition is null."));

            UiCreateResult<IUiButtonHandle> result = backend.CreateButton(ownerId, definition);
            if (result.Succeeded && result.Value != null)
                handles.Add(result.Value);
            return Task.FromResult(result);
        }

        public Task<UiCreateResult<IUiMenuButtonHandle>> CreateMenuButtonAsync(
            UiMenuButtonDefinition definition,
            CancellationToken cancellationToken = default)
        {
            if (disposed)
                return Task.FromResult(UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI scope is disposed."));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.Cancelled, "UI creation was cancelled."));
            if (definition == null)
                return Task.FromResult(UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button definition is null."));

            UiCreateResult<IUiMenuButtonHandle> result = backend.CreateMenuButton(ownerId, definition);
            if (result.Succeeded && result.Value != null)
                handles.Add(result.Value);
            return Task.FromResult(result);
        }

        public Task<UiCreateResult<IUiMenuButtonHandle>> CreateMainMenuButtonAsync(
            UiMenuButtonDefinition definition,
            CancellationToken cancellationToken = default)
        {
            if (disposed)
                return Task.FromResult(UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI scope is disposed."));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.Cancelled, "UI creation was cancelled."));
            if (definition == null)
                return Task.FromResult(UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button definition is null."));

            UiCreateResult<IUiMenuButtonHandle> result = backend.CreateMenuButton(ownerId, definition);
            if (result.Succeeded && result.Value != null)
                handles.Add(result.Value);
            return Task.FromResult(result);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            IDisposable[] current = new List<IDisposable>(handles).ToArray();
            handles.Clear();
            for (int index = current.Length - 1; index >= 0; index--)
                current[index].Dispose();
        }
    }

    internal sealed class FakeUiBackend : IUiBackend
    {
        private readonly List<FakeUiHandle> handles = new();
        private readonly bool requireParent;
        private bool disposed;

        internal FakeUiBackend(UiCapability capabilities = UiCapability.Button | UiCapability.MenuButton, bool requireParent = false)
        {
            this.requireParent = requireParent;
            Capabilities = new UiCapabilitySnapshot { GameVersion = new Version(0, 2, 53, 2), Available = capabilities };
        }

        public UiCapabilitySnapshot Capabilities { get; }
        internal IReadOnlyList<FakeUiHandle> Handles => handles;

        public UiCreateResult<IUiButtonHandle> CreateButton(string ownerId, UiButtonDefinition definition)
        {
            if (disposed)
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI backend is disposed.");
            if (!Capabilities.Supports(UiCapability.Button))
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.CapabilityUnavailable, "Button capability is unavailable.");
            if (requireParent && definition.Parent is null)
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.InvalidParent, "Button parent is null.");

            var handle = new FakeUiHandle(ownerId, definition.Text, definition.Enabled, false, definition.OnClick);
            handles.Add(handle);
            return UiCreateResult<IUiButtonHandle>.Success(handle);
        }

        public UiCreateResult<IUiMenuButtonHandle> CreateMenuButton(string ownerId, UiMenuButtonDefinition definition)
        {
            if (disposed)
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI backend is disposed.");
            if (!Capabilities.Supports(UiCapability.MenuButton))
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.CapabilityUnavailable, "Menu Button capability is unavailable.");
            if (requireParent && definition.Parent is null)
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button parent is null.");

            var handle = new FakeUiHandle(ownerId, definition.Text, definition.Enabled, definition.Selected, definition.OnClick);
            handles.Add(handle);
            return UiCreateResult<IUiMenuButtonHandle>.Success(handle);
        }

        public void SceneUnloaded()
        {
            foreach (FakeUiHandle handle in new List<FakeUiHandle>(handles))
                handle.Dispose();
            handles.Clear();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            SceneUnloaded();
        }

        internal sealed class FakeUiHandle : IUiMenuButtonHandle
        {
            private readonly Action? onClick;
            private bool disposed;
            private string text;
            private bool enabled;
            private bool selected;

            internal FakeUiHandle(string ownerId, string text, bool enabled, bool selected, Action? onClick)
            {
                OwnerId = ownerId;
                this.text = text;
                this.enabled = enabled;
                this.selected = selected;
                this.onClick = onClick;
            }

            internal string OwnerId { get; }
            internal int ClickCount { get; private set; }
            public bool IsDisposed => disposed;
            public bool Enabled { get => enabled && !disposed; set { if (!disposed) enabled = value; } }
            public string Text { get => text; set { if (!disposed) text = value ?? ""; } }
            public bool Selected { get => selected && !disposed; set { if (!disposed) selected = value; } }
            internal void Click()
            {
                if (disposed || !enabled)
                    return;
                ClickCount++;
                try { onClick?.Invoke(); }
                catch { }
            }
            public void Dispose() => disposed = true;
        }
    }
}
