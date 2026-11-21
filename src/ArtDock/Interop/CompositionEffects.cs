using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Graphics.Effects;
using WinRT;

namespace ArtDock.Interop;

/// <summary>
/// One Direct2D effect, described for the compositor: which effect, its properties in their
/// order, and what it draws from.
/// </summary>
/// <remarks>
/// <para>
/// The compositor takes a picture's recipe as a graph of these and runs it itself, every
/// frame, on whatever is behind a visual — which is the only way to blend with the desktop
/// rather than paint over it. Windows' own acrylic is such a graph, and the taskbar's is
/// rebuilt from these in <see cref="TaskbarAcrylic"/>.
/// </para>
/// <para>
/// Win2D is where these normally come from, and it has no build for a desktop .NET app, so the
/// handful needed are described by hand. The compositor asks an effect what it is through
/// <c>IGraphicsEffectD2D1Interop</c>, a plain COM interface the WinRT projection knows nothing
/// of — so it is answered beside the projection's own, through
/// <see cref="ICustomQueryInterface"/>, by a wrapper of .NET's making.
/// </para>
/// </remarks>
internal sealed partial class D2DEffect : IGraphicsEffect, IGraphicsEffectSource, ICustomQueryInterface
{
    public static readonly Guid Flood = new("61c23c20-ae69-4d8e-94cf-50078df638f2");
    public static readonly Guid GaussianBlur = new("1feb6d69-2fe6-4ac9-8c58-1d7f93e7a6a5");
    public static readonly Guid Blend = new("81c5b77b-13f8-4cdd-ad20-c890547ac65d");
    public static readonly Guid Opacity = new("811d79a4-de28-4454-8094-c64685f8bd4c");
    public static readonly Guid Border = new("2a2d49c0-4acf-43c7-8c6a-7c4a27874d27");
    public static readonly Guid Composite = new("48fc9f51-f6ac-48f1-8b58-3b28ac46f76d");
    public static readonly Guid ColorMatrix = new("921f03d6-641c-47df-852d-b4bb6153ae11");

    private const int Ok = 0;
    private const int InvalidArgument = unchecked((int)0x80070057);

    private static readonly Guid InteropId = new("2FC57384-A068-44D7-A331-30982FCF7177");
    private static readonly Guid PropertyValueId = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private readonly Guid _id;
    private readonly object[] _properties;
    private readonly IGraphicsEffectSource[] _sources;
    private readonly Description _description;

    /// <param name="id">The Direct2D effect's class id.</param>
    /// <param name="properties">
    /// Its properties, in Direct2D's order, each as the compositor wants it boxed: a
    /// <see cref="float"/>, a <see cref="uint"/> for an enumeration, a <see cref="bool"/>, or
    /// floats in a row for a colour or a matrix.
    /// </param>
    /// <param name="sources">Its inputs, in Direct2D's order.</param>
    public D2DEffect(Guid id, object[] properties, params IGraphicsEffectSource[] sources)
    {
        _id = id;
        _properties = properties;
        _sources = sources;
        _description = new Description(this);
    }

    public string Name { get; set; } = string.Empty;

    /// <summary>A colour as Direct2D's flood takes it: red, green, blue and alpha, each 0 to 1, not premultiplied.</summary>
    public static float[] Rgba(Windows.UI.Color color) =>
        [color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f];

    /// <remarks>
    /// Asked before the projection's own interfaces are looked through, for every one of them,
    /// so everything but the one question is passed on.
    /// </remarks>
    CustomQueryInterfaceResult ICustomQueryInterface.GetInterface(ref Guid iid, out nint ppv)
    {
        ppv = 0;
        if (iid != InteropId)
        {
            return CustomQueryInterfaceResult.NotHandled;
        }

        var unknown = Wrappers.GetOrCreateComInterfaceForObject(_description, CreateComInterfaceFlags.None);
        try
        {
            return Marshal.QueryInterface(unknown, in InteropId, out ppv) == Ok
                ? CustomQueryInterfaceResult.Handled
                : CustomQueryInterfaceResult.Failed;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// The effect's answers, as an object of their own: one that answered for itself would be
    /// asked this same question again by the wrapper made to answer it.
    /// </summary>
    [GeneratedComClass]
    private sealed partial class Description(D2DEffect effect) : IGraphicsEffectD2D1Interop
    {
        public int GetEffectId(out Guid id)
        {
            id = effect._id;
            return Ok;
        }

        /// <remarks>Only asked for a property that is to be animated, and none of these is.</remarks>
        public int GetNamedPropertyMapping(nint name, out uint index, out int mapping)
        {
            index = 0;
            mapping = 0;
            return InvalidArgument;
        }

        public int GetPropertyCount(out uint count)
        {
            count = (uint)effect._properties.Length;
            return Ok;
        }

        public int GetProperty(uint index, out nint value)
        {
            value = 0;
            if (index >= effect._properties.Length)
            {
                return InvalidArgument;
            }

            var boxed = MarshalInspectable<object>.FromManaged(effect._properties[index]);
            try
            {
                return Marshal.QueryInterface(boxed, in PropertyValueId, out value);
            }
            finally
            {
                Marshal.Release(boxed);
            }
        }

        public int GetSource(uint index, out nint source)
        {
            source = 0;
            if (index >= effect._sources.Length)
            {
                return InvalidArgument;
            }

            source = MarshalInterface<IGraphicsEffectSource>.FromManaged(effect._sources[index]);
            return Ok;
        }

        public int GetSourceCount(out uint count)
        {
            count = (uint)effect._sources.Length;
            return Ok;
        }
    }
}

/// <summary>How the compositor asks an effect what it is — <c>windows.graphics.effects.interop.h</c>.</summary>
[GeneratedComInterface]
[Guid("2FC57384-A068-44D7-A331-30982FCF7177")]
internal partial interface IGraphicsEffectD2D1Interop
{
    [PreserveSig]
    int GetEffectId(out Guid id);

    [PreserveSig]
    int GetNamedPropertyMapping(nint name, out uint index, out int mapping);

    [PreserveSig]
    int GetPropertyCount(out uint count);

    [PreserveSig]
    int GetProperty(uint index, out nint value);

    [PreserveSig]
    int GetSource(uint index, out nint source);

    [PreserveSig]
    int GetSourceCount(out uint count);
}
