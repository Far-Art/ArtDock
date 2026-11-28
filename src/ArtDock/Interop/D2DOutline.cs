using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Windows;
using ArtDock.Dock;
using Windows.Graphics;

namespace ArtDock.Interop;

/// <summary>
/// A shape the compositor cannot draw for itself, handed to it as a Direct2D path — what a
/// <c>CompositionPath</c> is made from: the bar's outline, or a rounded rectangle with the bar's
/// outline taken out of it.
/// </summary>
/// <remarks>
/// <para>
/// The compositor has a rounded rectangle of its own but no other shape short of a path, and a
/// path is built from an <see cref="IGeometrySource2D"/> that answers, through
/// <c>IGeometrySource2DInterop</c>, with a Direct2D geometry. Win2D's <c>CanvasGeometry</c> is
/// the usual source and has no build for a desktop .NET app, so this answers instead, the way
/// <see cref="D2DEffect"/> answers for an effect.
/// </para>
/// <para>
/// The geometry is built when asked for, in whichever factory it is asked for in, and handed
/// over: the compositor keeps it, and nothing here holds on to it.
/// </para>
/// </remarks>
internal sealed partial class D2DOutline : IGeometrySource2D, ICustomQueryInterface
{
    private static readonly Guid InteropId = new("0657AF73-53FD-47CF-84FF-C8492D2A80A3");

    private const int Ok = 0;

    /// <summary>
    /// <c>D2D1_COMBINE_MODE_EXCLUDE</c>: the first shape, less the second. Three, after union,
    /// intersect and xor — 1, tried first, is intersect, and drew every ring as the bar itself.
    /// </summary>
    private const int Exclude = 3;

    /// <summary>
    /// How closely a combined shape's curves follow the originals, in device pixels: a tenth of
    /// one, finer than Direct2D's default quarter, so the hole's rounded edge does not facet.
    /// </summary>
    private const float Tolerance = 0.1f;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private readonly Shape _shape;
    private readonly Shape? _hole;
    private readonly Source _source;

    /// <summary>A rounded rectangle, of a corner shape or, for null, plain quarter circles.</summary>
    private readonly record struct Shape(Rect Bounds, double Radius, BarCorners? Corners);

    /// <summary>The bar's outline, or a plain rounded rectangle for null corners.</summary>
    public D2DOutline(Rect bounds, double radius, BarCorners? corners)
        : this(new Shape(bounds, radius, corners), hole: null)
    {
    }

    private D2DOutline(Shape shape, Shape? hole)
    {
        _shape = shape;
        _hole = hole;
        _source = new Source(this);
    }

    /// <summary>
    /// A plain rounded rectangle with a shape taken out of it — a ring of the bar's shadow with
    /// the bar itself taken out, wherever the two overlap.
    /// </summary>
    public static D2DOutline Holed(Rect bounds, double radius, Rect hole, double holeRadius, BarCorners? holeCorners) =>
        new(new Shape(bounds, radius, null), new Shape(hole, holeRadius, holeCorners));

    CustomQueryInterfaceResult ICustomQueryInterface.GetInterface(ref Guid iid, out nint ppv)
    {
        ppv = 0;
        if (iid != InteropId)
        {
            return CustomQueryInterfaceResult.NotHandled;
        }

        var unknown = Wrappers.GetOrCreateComInterfaceForObject(_source, CreateComInterfaceFlags.None);
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

    /// <summary>The outline's answers, as an object of its own — see <see cref="D2DEffect"/> for why.</summary>
    [GeneratedComClass]
    private sealed partial class Source(D2DOutline outline) : IGeometrySource2DInterop
    {
        public int GetGeometry(out nint value)
        {
            value = 0;
            var factory = Direct2D.Factory;
            return factory == 0 ? unchecked((int)0x80004005) : outline.Build(factory, out value);
        }

        public int TryGetGeometryUsingFactory(nint factory, out nint value)
        {
            value = 0;
            return factory == 0 ? Ok : outline.Build(factory, out value);
        }
    }

    /// <summary>The outline, traced into a Direct2D geometry sink.</summary>
    private readonly unsafe struct Sink(nint sink) : IOutlineSink
    {
        public void Begin(Point start) =>
            Direct2D.BeginFigure(sink, Direct2D.Point(start), 0); // D2D1_FIGURE_BEGIN_FILLED

        public void LineTo(Point end)
        {
            var point = Direct2D.Point(end);
            Direct2D.AddLines(sink, &point, 1);
        }

        public void BezierTo(Point handle1, Point handle2, Point end)
        {
            var bezier = new Direct2D.Bezier(Direct2D.Point(handle1), Direct2D.Point(handle2), Direct2D.Point(end));
            Direct2D.AddBeziers(sink, &bezier, 1);
        }
    }

    /// <summary>Builds the whole shape as a new geometry in <paramref name="factory"/>, owned by the caller.</summary>
    /// <remarks>
    /// A hole is taken out by Direct2D's own combine, not by tracing both shapes into one path:
    /// the shadow's rings are pushed down, so the bar reaches past the top of the innermost, and
    /// filled by winding or alternation a hole reaching past its ring is filled back in there.
    /// </remarks>
    private unsafe int Build(nint factory, out nint geometry)
    {
        geometry = 0;
        var hr = BuildPath(factory, _shape, out var outer);
        if (hr < 0 || _hole is not { } hole)
        {
            geometry = outer;
            return hr;
        }

        nint inner = 0, result = 0, sink = 0;
        try
        {
            hr = BuildPath(factory, hole, out inner);
            if (hr < 0)
            {
                return hr;
            }

            hr = Direct2D.CreatePathGeometry(factory, &result);
            if (hr < 0)
            {
                return hr;
            }

            hr = Direct2D.Open(result, &sink);
            if (hr < 0)
            {
                return hr;
            }

            hr = Direct2D.CombineWithGeometry(outer, inner, Exclude, Tolerance, sink);
            var closed = Direct2D.Close(sink);
            hr = hr < 0 ? hr : closed;
            if (hr < 0)
            {
                return hr;
            }

            geometry = result;
            result = 0;
            return Ok;
        }
        finally
        {
            Release(sink);
            Release(result);
            Release(inner);
            Release(outer);
        }
    }

    private static void Release(nint unknown)
    {
        if (unknown != 0)
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>Traces one shape into a new path geometry in <paramref name="factory"/>, owned by the caller.</summary>
    private static unsafe int BuildPath(nint factory, Shape shape, out nint geometry)
    {
        geometry = 0;
        nint path = 0;
        var hr = Direct2D.CreatePathGeometry(factory, &path);
        if (hr < 0)
        {
            return hr;
        }

        nint sink = 0;
        hr = Direct2D.Open(path, &sink);
        if (hr < 0)
        {
            Marshal.Release(path);
            return hr;
        }

        Direct2D.SetFillMode(sink, 1); // D2D1_FILL_MODE_WINDING
        var tracer = new Sink(sink);
        if (shape.Corners is { } corners)
        {
            BarOutline.Trace(shape.Bounds, shape.Radius, corners, ref tracer);
        }
        else
        {
            BarOutline.TraceRounded(shape.Bounds, shape.Radius, smoothing: 0, ref tracer);
        }

        Direct2D.EndFigure(sink, 1); // D2D1_FIGURE_END_CLOSED
        hr = Direct2D.Close(sink);
        Marshal.Release(sink);

        if (hr < 0)
        {
            Marshal.Release(path);
            return hr;
        }

        geometry = path;
        return Ok;
    }
}

/// <summary>How the compositor asks a path's source for its geometry — <c>windows.graphics.interop.h</c>.</summary>
[GeneratedComInterface]
[Guid("0657AF73-53FD-47CF-84FF-C8492D2A80A3")]
internal partial interface IGeometrySource2DInterop
{
    [PreserveSig]
    int GetGeometry(out nint value);

    [PreserveSig]
    int TryGetGeometryUsingFactory(nint factory, out nint value);
}

/// <summary>
/// The little of Direct2D a path takes: a factory, and the path geometry and sink methods,
/// called through their vtables.
/// </summary>
internal static unsafe class Direct2D
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Point2F(float X, float Y);

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Bezier(Point2F Point1, Point2F Point2, Point2F Point3);

    public static Point2F Point(Point point) => new((float)point.X, (float)point.Y);

    private static readonly Lazy<nint> LazyFactory = new(CreateFactory);

    /// <summary>The process's factory, made the first time it is asked for; 0 where Direct2D will not make one.</summary>
    public static nint Factory => LazyFactory.Value;

    private static nint CreateFactory()
    {
        var iid = new Guid("06152247-6f50-465a-9245-118bfd3b6007"); // ID2D1Factory
        try
        {
            // Multithreaded: the compositor may ask for the geometry from a thread of its own.
            return D2D1CreateFactory(1, &iid, null, out var factory) < 0 ? 0 : factory;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private static void** Vtable(nint self) => *(void***)self;

    /// <summary><c>ID2D1Factory::CreatePathGeometry</c>, slot 10.</summary>
    public static int CreatePathGeometry(nint factory, nint* path) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtable(factory)[10])(factory, path);

    /// <summary>
    /// <c>ID2D1Geometry::CombineWithGeometry</c>, slot 11: this geometry combined with another
    /// by <paramref name="mode"/>, untransformed, written into <paramref name="sink"/>.
    /// </summary>
    public static int CombineWithGeometry(nint geometry, nint input, int mode, float tolerance, nint sink) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void*, float, nint, int>)Vtable(geometry)[11])(
            geometry, input, mode, null, tolerance, sink);

    /// <summary><c>ID2D1PathGeometry::Open</c>, slot 17.</summary>
    public static int Open(nint path, nint* sink) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtable(path)[17])(path, sink);

    /// <summary><c>ID2D1SimplifiedGeometrySink::SetFillMode</c>, slot 3.</summary>
    public static void SetFillMode(nint sink, int mode) =>
        ((delegate* unmanaged[Stdcall]<nint, int, void>)Vtable(sink)[3])(sink, mode);

    /// <summary><c>ID2D1SimplifiedGeometrySink::BeginFigure</c>, slot 5.</summary>
    public static void BeginFigure(nint sink, Point2F start, int begin) =>
        ((delegate* unmanaged[Stdcall]<nint, Point2F, int, void>)Vtable(sink)[5])(sink, start, begin);

    /// <summary><c>ID2D1SimplifiedGeometrySink::AddLines</c>, slot 6.</summary>
    public static void AddLines(nint sink, Point2F* points, uint count) =>
        ((delegate* unmanaged[Stdcall]<nint, Point2F*, uint, void>)Vtable(sink)[6])(sink, points, count);

    /// <summary><c>ID2D1SimplifiedGeometrySink::AddBeziers</c>, slot 7.</summary>
    public static void AddBeziers(nint sink, Bezier* beziers, uint count) =>
        ((delegate* unmanaged[Stdcall]<nint, Bezier*, uint, void>)Vtable(sink)[7])(sink, beziers, count);

    /// <summary><c>ID2D1SimplifiedGeometrySink::EndFigure</c>, slot 8.</summary>
    public static void EndFigure(nint sink, int end) =>
        ((delegate* unmanaged[Stdcall]<nint, int, void>)Vtable(sink)[8])(sink, end);

    /// <summary><c>ID2D1SimplifiedGeometrySink::Close</c>, slot 9.</summary>
    public static int Close(nint sink) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Vtable(sink)[9])(sink);

    [DllImport("d2d1.dll", ExactSpelling = true)]
    private static extern int D2D1CreateFactory(int factoryType, Guid* riid, void* options, out nint factory);
}
