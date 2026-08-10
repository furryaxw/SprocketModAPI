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
        Button = 1,
        MenuButton = 2
    }

    public sealed class UiCapabilitySnapshot
    {
        public Version GameVersion { get; init; } = new(0, 0);
        public UiCapability Available { get; init; }
        public bool IsSupported => Available != UiCapability.None;
        public bool Supports(UiCapability capability) => (Available & capability) == capability;
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

    public sealed class UiButtonDefinition
    {
        public Transform? Parent { get; init; }
        public string Text { get; init; } = "";
        public bool Enabled { get; init; } = true;
        public Vector2? Size { get; init; }
        public Vector2? AnchoredPosition { get; init; }
        public Action? OnClick { get; init; }
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

    public interface IUiButtonHandle : IDisposable
    {
        bool IsDisposed { get; }
        bool Enabled { get; set; }
        string Text { get; set; }
    }

    public interface IUiMenuButtonHandle : IUiButtonHandle
    {
        bool Selected { get; set; }
    }

    public interface IUiScope : IDisposable
    {
        Task<UiCreateResult<IUiButtonHandle>> CreateButtonAsync(UiButtonDefinition definition, CancellationToken cancellationToken = default);
        Task<UiCreateResult<IUiMenuButtonHandle>> CreateMenuButtonAsync(UiMenuButtonDefinition definition, CancellationToken cancellationToken = default);
        Task<UiCreateResult<IUiMenuButtonHandle>> CreateMainMenuButtonAsync(UiMenuButtonDefinition definition, CancellationToken cancellationToken = default);
    }

    public interface IUiService
    {
        UiCapabilitySnapshot Capabilities { get; }
        IUiScope CreateScope(UiOwnerDefinition owner);
    }
}
