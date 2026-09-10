using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Reads and writes the native document format: a small JSON file that keeps everything
/// SVG export throws away - glue between connectors and shapes, z-order, and the exact
/// colours and sizes a shape was given.
///
/// The records below are deliberately separate from the shape classes, so the shape
/// classes can be renamed or reorganised without invalidating files already on disk.
/// </summary>
public static partial class DiagramFile
{
    public const string FormatId = "avasvgmaker.diagram";
    public const string Extension = "avadiag";
    /// <summary>
    /// 2 added connection ports and routing, 3 hand-placed bends, 4 the line style,
    /// 5 containers, 6 multiple pages, 7 a paper size per page, 8 lane heights,
    /// 9 the font a label is in, 10 grouping, 11 the margin guide, 12 rotation,
    /// 13 shapes carrying an outline of their own, 14 a connector's label moved by hand.
    /// Older files still load.
    /// </summary>
    public const int CurrentVersion = 16;

    /// <summary>
    /// Serialisation is generated at build time rather than discovered by reflection, so the
    /// format still works when the app is compiled ahead of time - a reflection-based
    /// serialiser has nothing to reflect over once the trimmer has been through it.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(DocumentRecord))]
    private partial class Records : JsonSerializerContext;

    #region On-disk records

    private sealed class DocumentRecord
    {
        public string Format { get; set; } = FormatId;
        public int Version { get; set; } = CurrentVersion;
        /// <summary>
        /// The paper every page was on, up to version 6, when the size belonged to the
        /// document. Still read, as the size for every page of an older file; no longer
        /// written, because each page now carries its own.
        /// </summary>
        public double PageWidth { get; set; }

        public double PageHeight { get; set; }

        /// <summary>Version 6 onwards. Version 5 and earlier wrote a single page's shapes below.</summary>
        public List<PageRecord>? Pages { get; set; }

        /// <summary>How every version up to 5 stored its one page. Still read; no longer written.</summary>
        public List<ShapeRecord>? Shapes { get; set; }
    }

    private sealed class PageRecord
    {
        public string Name { get; set; } = "Page 1";

        /// <summary>Version 7 onwards. Absent in older files, which kept one size per document.</summary>
        public double? Width { get; set; }

        public double? Height { get; set; }

        /// <summary>Version 11 onwards, and omitted when there is no margin guide to draw.</summary>
        public double? Margin { get; set; }

        public List<ShapeRecord> Shapes { get; set; } = [];
    }

    /// <summary>One shape. List order is z-order, and <see cref="Id"/> is what connectors glue to.</summary>
    private sealed class ShapeRecord
    {
        public int Id { get; set; }
        public string Kind { get; set; } = nameof(ShapeKind.Rectangle);

        /// <summary>Omitted for connectors, which are defined by their end points instead.</summary>
        public double? X { get; set; }

        public double? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public string? Text { get; set; }
        public string? Fill { get; set; }
        public string? Stroke { get; set; }
        public string? TextColor { get; set; }
        public double StrokeThickness { get; set; } = 2;

        /// <summary>Solid is what every shape was before version 4, and so the default.</summary>
        public string StrokeStyle { get; set; } = nameof(Models.StrokeStyle.Solid);

        public double FontSize { get; set; } = 13;

        /// <summary>
        /// Version 9 onwards, and each written only when it differs from what every shape had
        /// before it existed - so an ordinary label adds nothing to the file.
        /// </summary>
        public string? FontName { get; set; }

        public bool? Bold { get; set; }

        public bool? Italic { get; set; }

        /// <summary>Centred is what every label was before version 9, and so the default.</summary>
        public string? TextAlign { get; set; }

        /// <summary>The container this shape sits in, by id. Absent when it sits on the page.</summary>
        public int? ContainerId { get; set; }

        /// <summary>
        /// Which group the shape is in. Version 10 onwards, and absent for a shape that is not
        /// in one - which, in most drawings, is all of them.
        /// </summary>
        public int? GroupId { get; set; }

        /// <summary>Degrees clockwise. Version 12 onwards, and absent for a shape sitting upright.</summary>
        public double? Rotation { get; set; }

        /// <summary>
        /// A unit-square outline, for a shape that carries one of its own rather than taking a
        /// stencil's. Version 13 onwards, and written only for those - an imported path.
        /// </summary>
        public string? Outline { get; set; }

        /// <summary>
        /// Markings over that outline that are drawn but not filled. Version 15 onwards, and
        /// written only for a path that has any.
        /// </summary>
        public string? Detail { get; set; }

        /// <summary>
        /// Which edge the label hugs down the shape, when it is not the middle. Version 16
        /// onwards, and written only when it is not.
        /// </summary>
        public string? TextVerticalAlign { get; set; }

        /// <summary>
        /// Where the label sits, as fractions of the shape - left, top, width, height. Version
        /// 16 onwards, and written only for a shape whose label is not simply its own box.
        /// </summary>
        public double[]? TextFrame { get; set; }

        /// <summary>
        /// A lane's share of its pool. Version 8 onwards, and only written for a lane that
        /// does not hold the standard single share - so an evenly divided pool, which is most
        /// of them, writes nothing at all.
        /// </summary>
        public double? LaneShare { get; set; }

        public ConnectorRecord? Connector { get; set; }
    }

    private sealed class ConnectorRecord
    {
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }

        /// <summary>The shape this end is glued to, or null when it floats free on the page.</summary>
        public int? StartShapeId { get; set; }

        public int? EndShapeId { get; set; }
        public string StartCap { get; set; } = nameof(EndCapStyle.None);
        public string EndCap { get; set; } = nameof(EndCapStyle.Arrow);

        /// <summary>Connection point index, or -1. Absent in version 1 files, which had none.</summary>
        public int StartPort { get; set; } = -1;

        public int EndPort { get; set; } = -1;

        /// <summary>Straight is the version 1 behaviour, and so the default when absent.</summary>
        public string Routing { get; set; } = nameof(ConnectorRouting.Straight);

        /// <summary>Bends placed by hand, as x,y pairs. Absent when the connector routes itself.</summary>
        public double[]? Waypoints { get; set; }

        /// <summary>
        /// How far the label has been dragged from where it would sit. Version 14 onwards,
        /// and absent for a label left where the line put it.
        /// </summary>
        public double[]? LabelOffset { get; set; }
    }

    #endregion

    /// <summary>The document as a JSON string - used for undo snapshots.</summary>
    public static string ToJson(DiagramDocument document)
    {
        using var stream = new MemoryStream();
        Write(document, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static DiagramDocument FromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return Read(stream);
    }

    public static void Write(DiagramDocument document, Stream stream)
    {
        // Ids are handed out in reading order, across the whole document, so the file stays
        // readable and no two shapes can collide even though references never cross a page.
        var ids = new Dictionary<DiagramShape, int>();
        var next = 1;

        foreach (var page in document.Pages)
        foreach (var shape in page.Shapes)
            ids[shape] = next++;

        var record = new DocumentRecord { Pages = [] };

        foreach (var page in document.Pages)
        {
            var pageRecord = new PageRecord
            {
                Name = page.Name,
                Width = page.Width,
                Height = page.Height,
                Margin = page.Margin > 0 ? page.Margin : null
            };

            foreach (var shape in page.Shapes)
                pageRecord.Shapes.Add(ToRecord(shape, ids));

            record.Pages.Add(pageRecord);
        }

        JsonSerializer.Serialize(stream, record, Records.Default.DocumentRecord);
    }

    /// <summary>
    /// Deep-copies shapes by taking them through the format, which rebuilds glue and
    /// containment among the copies instead of leaving them pointing at the originals.
    /// </summary>
    public static List<DiagramShape> CopyOf(IReadOnlyList<DiagramShape> shapes)
    {
        if (shapes.Count == 0)
            return [];

        var slice = new DiagramDocument();
        slice.Shapes.AddRange(shapes);

        return [.. FromJson(ToJson(slice)).Shapes];
    }

    private static ShapeRecord ToRecord(DiagramShape shape, IReadOnlyDictionary<DiagramShape, int> ids)
    {
        var isConnector = shape is ConnectorShape;
        var bounds = shape.Bounds;

        var record = new ShapeRecord
        {
            Id = ids[shape],
            Kind = shape.Kind.ToString(),
            X = isConnector ? null : bounds.X,
            Y = isConnector ? null : bounds.Y,
            Width = isConnector ? null : bounds.Width,
            Height = isConnector ? null : bounds.Height,
            Text = string.IsNullOrEmpty(shape.Text) ? null : shape.Text,
            Fill = shape.Fill.ToString(),
            Stroke = shape.Stroke.ToString(),
            TextColor = shape.TextColor.ToString(),
            StrokeThickness = shape.StrokeThickness,
            StrokeStyle = shape.StrokeStyle.ToString(),
            FontSize = shape.FontSize,
            FontName = string.IsNullOrEmpty(shape.FontName) ? null : shape.FontName,
            Bold = shape.Bold ? true : null,
            Italic = shape.Italic ? true : null,
            TextAlign = shape.TextAlign == Models.TextAlign.Center ? null : shape.TextAlign.ToString(),
            ContainerId = IdOf(shape.Container, ids),
            GroupId = shape.GroupId == 0 ? null : shape.GroupId,
            Rotation = shape.IsRotated ? shape.Rotation : null,
            Outline = shape is PathShape outlined ? outlined.Outline : null,
            Detail = shape is PathShape marked ? marked.Detail : null,
            TextVerticalAlign = shape.TextVerticalAlign == Models.TextVerticalAlign.Middle
                ? null
                : shape.TextVerticalAlign.ToString(),
            TextFrame = shape.TextFrame is { } frame
                ? [frame.X, frame.Y, frame.Width, frame.Height]
                : null,
            LaneShare = shape is ContainerShape { Kind: ShapeKind.Lane } lane
                        && Math.Abs(lane.LaneShare - 1) > 1e-9
                ? lane.LaneShare
                : null
        };

        if (shape is ConnectorShape connector)
        {
            var startId = IdOf(connector.StartShape, ids);
            var endId = IdOf(connector.EndShape, ids);

            // When only part of the page is being written - copying a selection - an end
            // glued to a shape left behind is frozen where it currently sits, so the
            // pasted connector keeps its shape instead of springing back to a stale point.
            var start = connector.StartShape is not null && startId is null ? connector.ResolvedStart : connector.Start;
            var end = connector.EndShape is not null && endId is null ? connector.ResolvedEnd : connector.End;

            record.Connector = new ConnectorRecord
            {
                StartX = start.X,
                StartY = start.Y,
                EndX = end.X,
                EndY = end.Y,
                StartShapeId = startId,
                EndShapeId = endId,
                StartCap = connector.StartCap.ToString(),
                EndCap = connector.EndCap.ToString(),
                StartPort = connector.StartPort,
                EndPort = connector.EndPort,
                Routing = connector.Routing.ToString(),
                Waypoints = connector.Waypoints.Count == 0
                    ? null
                    : connector.Waypoints.SelectMany(point => new[] { point.X, point.Y }).ToArray(),
                LabelOffset = connector.LabelOffset == default
                    ? null
                    : [connector.LabelOffset.X, connector.LabelOffset.Y]
            };
        }

        return record;
    }

    private static int? IdOf(DiagramShape? shape, IReadOnlyDictionary<DiagramShape, int> ids) =>
        shape is not null && ids.TryGetValue(shape, out var id) ? id : null;

    public static DiagramDocument Read(Stream stream)
    {
        var record = JsonSerializer.Deserialize(stream, Records.Default.DocumentRecord)
                     ?? throw new InvalidDataException("The file is empty.");

        if (!string.Equals(record.Format, FormatId, StringComparison.Ordinal))
            throw new InvalidDataException("That is not an AVASvgMaker diagram.");

        if (record.Version > CurrentVersion)
            throw new InvalidDataException(
                $"The file was written by a newer version of AVASvgMaker (file version {record.Version}).");

        var document = new DiagramDocument();

        // Up to version 5 a document was one page, written without a page record around it.
        var pageRecords = record.Pages is { Count: > 0 }
            ? record.Pages
            : [new PageRecord { Shapes = record.Shapes ?? [] }];

        var pages = new List<DiagramPage>();

        for (var p = 0; p < pageRecords.Count; p++)
        {
            var pageRecord = pageRecords[p];
            var page = new DiagramPage(string.IsNullOrWhiteSpace(pageRecord.Name)
                ? $"Page {p + 1}"
                : pageRecord.Name);

            // Version 7 onwards a page carries its own paper. Before that the document did,
            // so an older file puts the one size it has on every page it has.
            var width = pageRecord.Width ?? record.PageWidth;
            var height = pageRecord.Height ?? record.PageHeight;

            if (width > 0 && height > 0)
            {
                page.Width = width;
                page.Height = height;
            }

            page.Margin = Math.Max(0, pageRecord.Margin ?? 0);

            // First pass builds the shapes, second pass glues connectors to them, so a
            // connector can reference a shape that is drawn above it. The table is per page,
            // because nothing on one page may reference anything on another.
            var byId = new Dictionary<int, DiagramShape>();

            foreach (var shapeRecord in pageRecord.Shapes)
            {
                var shape = FromRecord(shapeRecord);
                page.Shapes.Add(shape);
                byId[shapeRecord.Id] = shape;
            }

            // Containment is resolved in the same second pass as glue, and for the same
            // reason: a shape can name a container that has not been built yet.
            for (var i = 0; i < pageRecord.Shapes.Count; i++)
                page.Shapes[i].Container = Glued(pageRecord.Shapes[i].ContainerId, byId);

            for (var i = 0; i < pageRecord.Shapes.Count; i++)
            {
                if (pageRecord.Shapes[i].Connector is not { } connectorRecord)
                    continue;

                if (page.Shapes[i] is not ConnectorShape connector)
                    continue;

                connector.StartShape = Glued(connectorRecord.StartShapeId, byId);
                connector.EndShape = Glued(connectorRecord.EndShapeId, byId);
            }

            pages.Add(page);
        }

        document.SetPages(pages);
        document.MarkSaved();
        return document;
    }

    private static DiagramShape? Glued(int? id, IReadOnlyDictionary<int, DiagramShape> byId) =>
        id is { } value && byId.TryGetValue(value, out var shape) ? shape : null;

    private static DiagramShape FromRecord(ShapeRecord record)
    {
        var kind = Parse(record.Kind, ShapeKind.Rectangle);
        var bounds = new Rect(record.X ?? 0, record.Y ?? 0, record.Width ?? 0, record.Height ?? 0);

        DiagramShape shape;

        if (kind == ShapeKind.Connector)
        {
            var connectorRecord = record.Connector ?? new ConnectorRecord();

            shape = new ConnectorShape(
                new Point(connectorRecord.StartX, connectorRecord.StartY),
                new Point(connectorRecord.EndX, connectorRecord.EndY))
            {
                StartCap = Parse(connectorRecord.StartCap, EndCapStyle.None),
                EndCap = Parse(connectorRecord.EndCap, EndCapStyle.Arrow),
                StartPort = connectorRecord.StartPort,
                EndPort = connectorRecord.EndPort,
                Routing = Parse(connectorRecord.Routing, ConnectorRouting.Straight),
                Waypoints = ToPoints(connectorRecord.Waypoints),
                LabelOffset = connectorRecord.LabelOffset is { Length: 2 } moved
                    ? new Vector(moved[0], moved[1])
                    : default
            };
        }
        else if (kind == ShapeKind.Path)
        {
            shape = new PathShape(bounds)
            {
                Outline = string.IsNullOrWhiteSpace(record.Outline) ? PathShape.Fallback : record.Outline,
                Detail = string.IsNullOrWhiteSpace(record.Detail) ? null : record.Detail
            };
        }
        else
        {
            shape = ShapeFactory.Create(kind, bounds);
        }

        shape.Text = record.Text ?? string.Empty;
        shape.Fill = ToColor(record.Fill, DiagramShape.DefaultFill);
        shape.Stroke = ToColor(record.Stroke, DiagramShape.DefaultStroke);
        shape.TextColor = ToColor(record.TextColor, DiagramShape.DefaultTextColor);
        shape.StrokeThickness = record.StrokeThickness > 0 ? record.StrokeThickness : 2;
        shape.StrokeStyle = Parse(record.StrokeStyle, Models.StrokeStyle.Solid);
        shape.FontSize = record.FontSize > 0 ? record.FontSize : 13;
        shape.FontName = record.FontName ?? string.Empty;
        shape.Bold = record.Bold ?? false;
        shape.Italic = record.Italic ?? false;
        shape.TextAlign = Parse(record.TextAlign, Models.TextAlign.Center);
        shape.TextVerticalAlign = Parse(record.TextVerticalAlign, Models.TextVerticalAlign.Middle);
        shape.GroupId = record.GroupId ?? 0;
        shape.Rotation = record.Rotation ?? 0;

        // Four numbers, or nothing at all for a label that simply fills its shape.
        if (record.TextFrame is { Length: 4 } frame)
            shape.TextFrame = new Rect(frame[0], frame[1], frame[2], frame[3]);

        // Absent before version 8, and absent since for any lane on the standard share.
        if (shape is ContainerShape { Kind: ShapeKind.Lane } lane && record.LaneShare is { } share && share > 0)
            lane.LaneShare = share;

        return shape;
    }

    private static IReadOnlyList<Point> ToPoints(double[]? values)
    {
        if (values is null || values.Length < 2)
            return [];

        var points = new List<Point>();

        for (var i = 0; i + 1 < values.Length; i += 2)
            points.Add(new Point(values[i], values[i + 1]));

        return points;
    }

    private static T Parse<T>(string? text, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(text, out var value) ? value : fallback;

    private static Color ToColor(string? text, Color fallback) =>
        Color.TryParse(text, out var color) ? color : fallback;
}
