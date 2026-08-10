using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SprocketModAPI
{
    public sealed class UiOwnerDefinition
    {
        public string ModId { get; init; } = "";
        public string DisplayName { get; init; } = "";
    }

    [Flags]
    public enum UiCapability
    {
        None = 0,
        MenuButton = 2
    }

    public sealed class UiCapabilitySnapshot
    {
        public Version GameVersion { get; init; } = new(0, 0);
        public UiCapability Available { get; init; }
        public string SceneName { get; init; } = "";
        public bool IsMainMenuReady { get; init; }
        public int MenuGeneration { get; init; }
        public bool IsSupported => Available != UiCapability.None;
        public bool Supports(UiCapability capability) => (Available & capability) == capability;
    }

    public sealed class UiStatusChangedEventArgs : EventArgs
    {
        public UiStatusChangedEventArgs(UiCapabilitySnapshot previous, UiCapabilitySnapshot current)
        { Previous = previous; Current = current; }
        public UiCapabilitySnapshot Previous { get; }
        public UiCapabilitySnapshot Current { get; }
    }

    public enum UiFailureCode
    {
        None,
        UnsupportedGameVersion,
        CapabilityUnavailable,
        SceneUnavailable,
        TemplateNotFound,
        InvalidParent,
        OwnerDisposed,
        Cancelled,
        CreationFailed
    }

    public readonly struct UiCreateResult<T> where T : class
    {
        private UiCreateResult(T? value, UiFailureCode failure, string message)
        {
            Value = value;
            Failure = failure;
            Message = message;
        }

        public T? Value { get; }
        public UiFailureCode Failure { get; }
        public string Message { get; }
        public bool Succeeded => Value != null && Failure == UiFailureCode.None;

        public static UiCreateResult<T> Success(T value) => new(value, UiFailureCode.None, "");
        public static UiCreateResult<T> Failed(UiFailureCode failure, string message) => new(null, failure, message);
    }

    public sealed class UiMenuButtonDefinition
    {
        public Transform? Parent { get; init; }
        public string Text { get; init; } = "";
        public bool Enabled { get; init; } = true;
        public bool Selected { get; init; }
        public string? BelowNativeButtonText { get; init; }
        public Vector2? Size { get; init; }
        public Vector2? AnchoredPosition { get; init; }
        public Action? OnClick { get; init; }
    }

    // Retained for binary compatibility with existing Menu Button consumers; no ordinary Button factory remains.
    public interface IUiButtonHandle : IDisposable
    {
        bool IsDisposed { get; }
        bool Enabled { get; set; }
        string Text { get; set; }
    }

    public interface IUiMenuButtonHandle : IUiButtonHandle
    {
        // Explicit redeclarations preserve the interface slots used by existing consumers.
        new bool IsDisposed { get; }
        new bool Enabled { get; set; }
        new string Text { get; set; }
        bool Selected { get; set; }
    }

    public interface IUiScope : IDisposable
    {
        Task<UiCreateResult<IUiMenuButtonHandle>> CreateMenuButtonAsync(UiMenuButtonDefinition definition, CancellationToken cancellationToken = default);
        Task<UiCreateResult<IUiMenuButtonHandle>> CreateMainMenuButtonAsync(UiMenuButtonDefinition definition, CancellationToken cancellationToken = default);
    }

    public interface IUiService
    {
        UiCapabilitySnapshot Capabilities { get; }
        event EventHandler<UiStatusChangedEventArgs> StatusChanged;
        IUiScope CreateScope(UiOwnerDefinition owner);
    }
}
