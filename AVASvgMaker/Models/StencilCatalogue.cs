using System.Collections.Generic;
using System.Linq;

namespace AVASvgMaker.Models;

/// <summary>
/// Every stencil the toolbox offers. Outlines are written in a unit square - see
/// <see cref="StencilPath"/> - so a shape is a line of data rather than a class. The few
/// entries with no outline are drawn by their own class instead, where the drawing needs
/// something the path language deliberately does not do.
/// </summary>
public static class StencilCatalogue
{
    /// <summary>A circle filling the box, as four beziers.</summary>
    private const string Circle =
        "M 0.5,0 C 0.776,0 1,0.224 1,0.5 C 1,0.776 0.776,1 0.5,1 " +
        "C 0.224,1 0,0.776 0,0.5 C 0,0.224 0.224,0 0.5,0 Z";

    private const string Rounded =
        "M 0.12,0 L 0.88,0 Q 1,0 1,0.12 L 1,0.88 Q 1,1 0.88,1 " +
        "L 0.12,1 Q 0,1 0,0.88 L 0,0.12 Q 0,0 0.12,0 Z";

    private const string Diamond = "M 0.5,0 L 1,0.5 L 0.5,1 L 0,0.5 Z";

    /// <summary>A circle of the given radius about the centre, for markers inside a shape.</summary>
    private static string Ring(double radius)
    {
        const double kappa = 0.5523;

        var k = radius * kappa;
        var t = (0.5 - radius).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var b = (0.5 + radius).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var less = (0.5 - k).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var more = (0.5 + k).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        return $"M 0.5,{t} C {more},{t} {b},{less} {b},0.5 C {b},{more} {more},{b} 0.5,{b} " +
               $"C {less},{b} {t},{more} {t},0.5 C {t},{less} {less},{t} 0.5,{t} Z";
    }

    public static readonly IReadOnlyList<Stencil> All =
    [
        // ---- Basic ----------------------------------------------------------------
        new(ShapeKind.Rectangle, StencilCategory.Basic, "Rectangle", Keywords: "box square process"),
        new(ShapeKind.RoundedRectangle, StencilCategory.Basic, "Rounded rectangle", Keywords: "box terminator"),
        new(ShapeKind.Ellipse, StencilCategory.Basic, "Ellipse", Keywords: "circle oval round"),
        new(ShapeKind.Triangle, StencilCategory.Basic, "Triangle", Keywords: "extract"),
        new(ShapeKind.RightTriangle, StencilCategory.Basic, "Right triangle",
            "M 0,0 L 0,1 L 1,1 Z"),
        new(ShapeKind.Diamond, StencilCategory.Basic, "Diamond", Keywords: "decision rhombus"),
        new(ShapeKind.Parallelogram, StencilCategory.Basic, "Parallelogram", Keywords: "data skew"),
        new(ShapeKind.Trapezoid, StencilCategory.Basic, "Trapezoid",
            "M 0.22,0 L 0.78,0 L 1,1 L 0,1 Z"),
        new(ShapeKind.Pentagon, StencilCategory.Basic, "Pentagon",
            "M 0.5,0 L 1,0.38 L 0.81,1 L 0.19,1 L 0,0.38 Z"),
        new(ShapeKind.Hexagon, StencilCategory.Basic, "Hexagon", Keywords: "preparation"),
        new(ShapeKind.Octagon, StencilCategory.Basic, "Octagon",
            "M 0.3,0 L 0.7,0 L 1,0.3 L 1,0.7 L 0.7,1 L 0.3,1 L 0,0.7 L 0,0.3 Z"),
        new(ShapeKind.Star, StencilCategory.Basic, "Star",
            "M 0.5,0 L 0.62,0.35 L 1,0.36 L 0.69,0.59 L 0.81,0.95 L 0.5,0.72 " +
            "L 0.19,0.95 L 0.31,0.59 L 0,0.36 L 0.38,0.35 Z"),
        new(ShapeKind.Cross, StencilCategory.Basic, "Cross",
            "M 0.35,0 L 0.65,0 L 0.65,0.35 L 1,0.35 L 1,0.65 L 0.65,0.65 " +
            "L 0.65,1 L 0.35,1 L 0.35,0.65 L 0,0.65 L 0,0.35 L 0.35,0.35 Z",
            Keywords: "plus add"),
        new(ShapeKind.BlockArrow, StencilCategory.Basic, "Arrow",
            "M 0,0.28 L 0.6,0.28 L 0.6,0.04 L 1,0.5 L 0.6,0.96 L 0.6,0.72 L 0,0.72 Z",
            Keywords: "block pointer"),
        new(ShapeKind.Chevron, StencilCategory.Basic, "Chevron",
            "M 0,0 L 0.76,0 L 1,0.5 L 0.76,1 L 0,1 L 0.24,0.5 Z",
            Keywords: "arrow step process"),
        new(ShapeKind.Cloud, StencilCategory.Basic, "Cloud",
            "M 0.26,0.9 C 0.06,0.9 0,0.72 0.11,0.6 C 0.02,0.44 0.16,0.26 0.32,0.32 " +
            "C 0.38,0.1 0.68,0.08 0.75,0.3 C 0.93,0.26 1.02,0.46 0.93,0.6 " +
            "C 1.02,0.74 0.94,0.9 0.78,0.9 Z",
            Keywords: "internet weather"),

        // ---- Arrows ---------------------------------------------------------------
        new(ShapeKind.BlockArrow, StencilCategory.Arrow, "Arrow right", Keywords: "east forward next"),
        new(ShapeKind.ArrowLeft, StencilCategory.Arrow, "Arrow left",
            "M 1,0.28 L 0.4,0.28 L 0.4,0.04 L 0,0.5 L 0.4,0.96 L 0.4,0.72 L 1,0.72 Z",
            Keywords: "west back previous"),
        new(ShapeKind.ArrowUp, StencilCategory.Arrow, "Arrow up",
            "M 0.28,1 L 0.28,0.4 L 0.04,0.4 L 0.5,0 L 0.96,0.4 L 0.72,0.4 L 0.72,1 Z",
            Keywords: "north rise increase"),
        new(ShapeKind.ArrowDown, StencilCategory.Arrow, "Arrow down",
            "M 0.28,0 L 0.28,0.6 L 0.04,0.6 L 0.5,1 L 0.96,0.6 L 0.72,0.6 L 0.72,0 Z",
            Keywords: "south fall decrease"),
        new(ShapeKind.DoubleArrow, StencilCategory.Arrow, "Double arrow",
            "M 0,0.5 L 0.22,0.06 L 0.22,0.3 L 0.78,0.3 L 0.78,0.06 L 1,0.5 " +
            "L 0.78,0.94 L 0.78,0.7 L 0.22,0.7 L 0.22,0.94 Z",
            Keywords: "both ways two-way exchange"),
        new(ShapeKind.DoubleArrowVertical, StencilCategory.Arrow, "Double arrow, vertical",
            "M 0.5,0 L 0.94,0.22 L 0.7,0.22 L 0.7,0.78 L 0.94,0.78 L 0.5,1 " +
            "L 0.06,0.78 L 0.3,0.78 L 0.3,0.22 L 0.06,0.22 Z",
            Keywords: "both ways two-way up down"),
        new(ShapeKind.BentArrow, StencilCategory.Arrow, "Bent arrow",
            "M 0,0.96 L 0.72,0.96 L 0.72,0.32 L 0.9,0.32 L 0.61,0 L 0.32,0.32 " +
            "L 0.5,0.32 L 0.5,0.68 L 0,0.68 Z",
            Keywords: "elbow turn corner right angle"),
        new(ShapeKind.CurvedArrow, StencilCategory.Arrow, "Curved arrow",
            "M 0.06,1 Q 0.06,0.26 0.6,0.22 L 0.6,0.02 L 1,0.34 L 0.6,0.66 L 0.6,0.46 " +
            "Q 0.34,0.5 0.34,1 Z",
            Keywords: "bend sweep"),
        new(ShapeKind.NotchedArrow, StencilCategory.Arrow, "Notched arrow",
            "M 0,0.28 L 0.6,0.28 L 0.6,0.04 L 1,0.5 L 0.6,0.96 L 0.6,0.72 L 0,0.72 L 0.14,0.5 Z",
            Keywords: "pentagon tail"),
        new(ShapeKind.Chevron, StencilCategory.Arrow, "Chevron", Keywords: "step process stage"),

        // ---- Callouts -------------------------------------------------------------
        new(ShapeKind.SpeechBubble, StencilCategory.Callout, "Speech bubble",
            "M 0.1,0 L 0.9,0 Q 1,0 1,0.12 L 1,0.6 Q 1,0.72 0.9,0.72 L 0.44,0.72 " +
            "L 0.22,1 L 0.3,0.72 L 0.1,0.72 Q 0,0.72 0,0.6 L 0,0.12 Q 0,0 0.1,0 Z",
            Keywords: "callout say talk comment balloon"),
        new(ShapeKind.OvalCallout, StencilCategory.Callout, "Oval callout",
            "M 0.5,0 C 0.78,0 1,0.17 1,0.36 C 1,0.55 0.78,0.72 0.5,0.72 L 0.4,0.72 " +
            "L 0.2,1 L 0.29,0.71 C 0.11,0.67 0,0.53 0,0.36 C 0,0.17 0.22,0 0.5,0 Z",
            Keywords: "callout balloon round"),
        new(ShapeKind.ThoughtBubble, StencilCategory.Callout, "Thought bubble",
            "M 0.3,0.7 C 0.1,0.7 0.04,0.53 0.14,0.42 C 0.06,0.27 0.2,0.11 0.34,0.17 " +
            "C 0.4,0 0.68,0 0.74,0.16 C 0.9,0.13 0.98,0.3 0.9,0.42 " +
            "C 0.98,0.55 0.9,0.7 0.76,0.7 Z " +
            "M 0.26,0.78 C 0.33,0.78 0.33,0.88 0.26,0.88 C 0.19,0.88 0.19,0.78 0.26,0.78 Z " +
            "M 0.13,0.91 C 0.18,0.91 0.18,1 0.13,1 C 0.08,1 0.08,0.91 0.13,0.91 Z",
            Keywords: "callout think cloud dream"),
        new(ShapeKind.RectangularCallout, StencilCategory.Callout, "Rectangular callout",
            "M 0,0 L 1,0 L 1,0.72 L 0.44,0.72 L 0.22,1 L 0.3,0.72 L 0,0.72 Z",
            Keywords: "callout box label pointer"),
        new(ShapeKind.AnnotationBracket, StencilCategory.Callout, "Annotation bracket",
            Detail: "M 0.4,0 L 0.12,0 L 0.12,0.44 L 0,0.5 L 0.12,0.56 L 0.12,1 L 0.4,1",
            Keywords: "brace note comment margin"),

        // ---- Flowchart ------------------------------------------------------------
        // The first six are plain geometry under a flowchart name; they are listed in both
        // places on purpose, because that is how they are looked for.
        new(ShapeKind.Rectangle, StencilCategory.Flowchart, "Process", Keywords: "step action box"),
        new(ShapeKind.RoundedRectangle, StencilCategory.Flowchart, "Terminator",
            Keywords: "start end begin stop"),
        new(ShapeKind.Diamond, StencilCategory.Flowchart, "Decision", Keywords: "branch if choice"),
        new(ShapeKind.Parallelogram, StencilCategory.Flowchart, "Data", Keywords: "input output io"),
        new(ShapeKind.Hexagon, StencilCategory.Flowchart, "Preparation", Keywords: "setup init"),
        new(ShapeKind.Cylinder, StencilCategory.Flowchart, "Database", Keywords: "store disk data"),
        new(ShapeKind.PredefinedProcess, StencilCategory.Flowchart, "Predefined process",
            "M 0,0 L 1,0 L 1,1 L 0,1 Z", "M 0.12,0 L 0.12,1 M 0.88,0 L 0.88,1",
            "subroutine call"),
        new(ShapeKind.ManualInput, StencilCategory.Flowchart, "Manual input",
            "M 0,0.22 L 1,0 L 1,1 L 0,1 Z", Keywords: "keyboard entry"),
        new(ShapeKind.ManualOperation, StencilCategory.Flowchart, "Manual operation",
            "M 0.16,0 L 0.84,0 L 1,1 L 0,1 Z"),
        new(ShapeKind.Document, StencilCategory.Flowchart, "Document",
            "M 0,0 L 1,0 L 1,0.84 C 0.75,1.06 0.25,0.62 0,0.84 Z",
            Keywords: "report paper"),
        new(ShapeKind.MultiDocument, StencilCategory.Flowchart, "Multi-document",
            "M 0,0.14 L 0.88,0.14 L 0.88,0.86 C 0.66,1.05 0.22,0.66 0,0.86 Z",
            "M 0.06,0.14 L 0.06,0.07 L 0.94,0.07 L 0.94,0.78 " +
            "M 0.12,0.07 L 0.12,0 L 1,0 L 1,0.71",
            "documents reports"),
        new(ShapeKind.OffPageReference, StencilCategory.Flowchart, "Off-page reference",
            "M 0,0 L 1,0 L 1,0.68 L 0.5,1 L 0,0.68 Z",
            Keywords: "link continue"),
        new(ShapeKind.OnPageConnector, StencilCategory.Flowchart, "On-page connector",
            "M 0.5,0 C 0.78,0 1,0.22 1,0.5 C 1,0.78 0.78,1 0.5,1 " +
            "C 0.22,1 0,0.78 0,0.5 C 0,0.22 0.22,0 0.5,0 Z",
            Keywords: "circle junction"),
        new(ShapeKind.Delay, StencilCategory.Flowchart, "Delay",
            "M 0,0 L 0.66,0 C 1.05,0 1.05,1 0.66,1 L 0,1 Z",
            Keywords: "wait pause"),
        new(ShapeKind.StoredData, StencilCategory.Flowchart, "Stored data",
            "M 0.16,0 L 1,0 C 0.84,0.3 0.84,0.7 1,1 L 0.16,1 C 0,0.7 0,0.3 0.16,0 Z"),
        new(ShapeKind.InternalStorage, StencilCategory.Flowchart, "Internal storage",
            "M 0,0 L 1,0 L 1,1 L 0,1 Z", "M 0,0.2 L 1,0.2 M 0.18,0 L 0.18,1",
            "memory"),
        new(ShapeKind.Merge, StencilCategory.Flowchart, "Merge",
            "M 0,0 L 1,0 L 0.5,1 Z", Keywords: "combine down"),
        new(ShapeKind.Extract, StencilCategory.Flowchart, "Extract",
            "M 0.5,0 L 1,1 L 0,1 Z", Keywords: "split up"),
        new(ShapeKind.SummingJunction, StencilCategory.Flowchart, "Summing junction",
            "M 0.5,0 C 0.78,0 1,0.22 1,0.5 C 1,0.78 0.78,1 0.5,1 " +
            "C 0.22,1 0,0.78 0,0.5 C 0,0.22 0.22,0 0.5,0 Z",
            "M 0.15,0.15 L 0.85,0.85 M 0.85,0.15 L 0.15,0.85",
            "and cross"),
        new(ShapeKind.Or, StencilCategory.Flowchart, "Or",
            "M 0.5,0 C 0.78,0 1,0.22 1,0.5 C 1,0.78 0.78,1 0.5,1 " +
            "C 0.22,1 0,0.78 0,0.5 C 0,0.22 0.22,0 0.5,0 Z",
            "M 0.5,0 L 0.5,1 M 0,0.5 L 1,0.5",
            "plus junction"),
        new(ShapeKind.Display, StencilCategory.Flowchart, "Display",
            "M 0.16,0 L 0.82,0 C 1.04,0.28 1.04,0.72 0.82,1 L 0.16,1 L 0,0.5 Z",
            Keywords: "screen monitor output"),
        new(ShapeKind.Card, StencilCategory.Flowchart, "Card",
            "M 0.18,0 L 1,0 L 1,1 L 0,1 L 0,0.24 Z", Keywords: "punch"),
        new(ShapeKind.Collate, StencilCategory.Flowchart, "Collate",
            "M 0,0 L 1,0 L 0,1 L 1,1 Z", Keywords: "hourglass"),
        new(ShapeKind.Sort, StencilCategory.Flowchart, "Sort",
            "M 0.5,0 L 1,0.5 L 0.5,1 L 0,0.5 Z", "M 0,0.5 L 1,0.5",
            "order diamond"),


        // ---- BPMN -----------------------------------------------------------------
        // Events are circles that differ only in their border, so the weight is part of the
        // stencil: a thin ring starts a process and a thick one ends it.
        new(ShapeKind.BpmnStartEvent, StencilCategory.Bpmn, "Start event",
            Circle, Keywords: "begin trigger bpmn", Defaults: new StencilDefaults(StrokeThickness: 1.5)),
        new(ShapeKind.BpmnIntermediateEvent, StencilCategory.Bpmn, "Intermediate event",
            Circle, Ring(0.38), "bpmn catch throw", new StencilDefaults(StrokeThickness: 1.5)),
        new(ShapeKind.BpmnEndEvent, StencilCategory.Bpmn, "End event",
            Circle, Keywords: "finish stop bpmn", Defaults: new StencilDefaults(StrokeThickness: 4)),
        new(ShapeKind.BpmnMessageEvent, StencilCategory.Bpmn, "Message event",
            Circle,
            "M 0.28,0.4 L 0.72,0.4 L 0.72,0.62 L 0.28,0.62 Z M 0.28,0.4 L 0.5,0.53 L 0.72,0.4",
            "bpmn envelope send receive", new StencilDefaults(StrokeThickness: 1.5)),
        new(ShapeKind.BpmnTimerEvent, StencilCategory.Bpmn, "Timer event",
            Circle,
            Ring(0.32) + " M 0.5,0.5 L 0.5,0.3 M 0.5,0.5 L 0.63,0.58",
            "bpmn clock wait delay", new StencilDefaults(StrokeThickness: 1.5)),

        // Activities are rounded rectangles that differ only by the marker on them.
        new(ShapeKind.BpmnTask, StencilCategory.Bpmn, "Task", Rounded, Keywords: "bpmn activity step"),
        new(ShapeKind.BpmnSubprocess, StencilCategory.Bpmn, "Subprocess",
            Rounded,
            "M 0.44,0.8 L 0.56,0.8 L 0.56,0.94 L 0.44,0.94 Z " +
            "M 0.5,0.83 L 0.5,0.91 M 0.46,0.87 L 0.54,0.87",
            "bpmn collapsed expand nested"),
        new(ShapeKind.BpmnUserTask, StencilCategory.Bpmn, "User task",
            Rounded,
            "M 0.13,0.105 C 0.155,0.105 0.175,0.125 0.175,0.15 C 0.175,0.175 0.155,0.195 0.13,0.195 " +
            "C 0.105,0.195 0.085,0.175 0.085,0.15 C 0.085,0.125 0.105,0.105 0.13,0.105 Z " +
            "M 0.06,0.3 C 0.06,0.215 0.2,0.215 0.2,0.3",
            "bpmn person manual human"),
        new(ShapeKind.BpmnServiceTask, StencilCategory.Bpmn, "Service task",
            Rounded,
            "M 0.13,0.08 L 0.13,0.24 M 0.05,0.16 L 0.21,0.16 " +
            "M 0.07,0.1 L 0.19,0.22 M 0.19,0.1 L 0.07,0.22",
            "bpmn gear automatic system"),
        new(ShapeKind.BpmnScriptTask, StencilCategory.Bpmn, "Script task",
            Rounded,
            "M 0.06,0.08 L 0.2,0.08 L 0.2,0.26 L 0.06,0.26 Z " +
            "M 0.09,0.13 L 0.17,0.13 M 0.09,0.17 L 0.17,0.17 M 0.09,0.21 L 0.15,0.21",
            "bpmn code document"),

        // Gateways are diamonds that differ only by the mark inside them.
        new(ShapeKind.BpmnExclusiveGateway, StencilCategory.Bpmn, "Exclusive gateway",
            Diamond, "M 0.36,0.36 L 0.64,0.64 M 0.64,0.36 L 0.36,0.64",
            "bpmn xor decision either choice"),
        new(ShapeKind.BpmnParallelGateway, StencilCategory.Bpmn, "Parallel gateway",
            Diamond, "M 0.5,0.28 L 0.5,0.72 M 0.28,0.5 L 0.72,0.5",
            "bpmn and fork join split"),
        new(ShapeKind.BpmnInclusiveGateway, StencilCategory.Bpmn, "Inclusive gateway",
            Diamond, Ring(0.22), "bpmn or any"),
        new(ShapeKind.BpmnEventGateway, StencilCategory.Bpmn, "Event gateway",
            Diamond, Ring(0.28) + " " + Ring(0.21), "bpmn event based wait race"),

        new(ShapeKind.BpmnDataObject, StencilCategory.Bpmn, "Data object",
            "M 0,0 L 0.72,0 L 1,0.22 L 1,1 L 0,1 Z", "M 0.72,0 L 0.72,0.22 L 1,0.22",
            "bpmn document artefact"),
        new(ShapeKind.Cylinder, StencilCategory.Bpmn, "Data store", Keywords: "bpmn database persist"),
        new(ShapeKind.BpmnGroup, StencilCategory.Bpmn, "Group",
            Rounded, Keywords: "bpmn boundary dashed region",
            Defaults: new StencilDefaults(StrokeStyle: StrokeStyle.Dashed, Hollow: true)),

        // Containers hold what is dropped into them - see ContainerShape.
        // Hollow, so what is dropped inside reads against the page rather than against a
        // second wash of colour - and so nested containers do not compound.
        new(ShapeKind.Pool, StencilCategory.Bpmn, "Pool",
            Keywords: "bpmn swimlane participant container",
            Defaults: new StencilDefaults(Hollow: true)),
        new(ShapeKind.Lane, StencilCategory.Bpmn, "Lane",
            Keywords: "bpmn swimlane role container",
            Defaults: new StencilDefaults(Hollow: true)),
        new(ShapeKind.ContainerBox, StencilCategory.Bpmn, "Container",
            Keywords: "group box region boundary",
            Defaults: new StencilDefaults(Hollow: true)),

        // ---- UML ------------------------------------------------------------------
        new(ShapeKind.UmlClass, StencilCategory.Uml, "Class",
            "M 0,0 L 1,0 L 1,1 L 0,1 Z", "M 0,0.34 L 1,0.34 M 0,0.66 L 1,0.66",
            "type compartments"),
        new(ShapeKind.UmlInterface, StencilCategory.Uml, "Interface",
            "M 0,0 L 1,0 L 1,1 L 0,1 Z", "M 0,0.28 L 1,0.28",
            "contract protocol"),
        new(ShapeKind.UmlPackage, StencilCategory.Uml, "Package",
            "M 0,0 L 0.4,0 L 0.46,0.14 L 1,0.14 L 1,1 L 0,1 Z",
            Keywords: "namespace folder module"),
        new(ShapeKind.UmlNote, StencilCategory.Uml, "Note",
            "M 0,0 L 0.8,0 L 1,0.2 L 1,1 L 0,1 Z", "M 0.8,0 L 0.8,0.2 L 1,0.2",
            "comment annotation"),
        new(ShapeKind.UmlActor, StencilCategory.Uml, "Actor",
            "M 0.5,0.04 C 0.61,0.04 0.61,0.26 0.5,0.26 C 0.39,0.26 0.39,0.04 0.5,0.04 Z",
            "M 0.5,0.26 L 0.5,0.66 M 0.16,0.4 L 0.84,0.4 " +
            "M 0.5,0.66 L 0.2,1 M 0.5,0.66 L 0.8,1",
            "user person stick figure"),
        new(ShapeKind.UmlUseCase, StencilCategory.Uml, "Use case",
            "M 0.5,0 C 0.78,0 1,0.22 1,0.5 C 1,0.78 0.78,1 0.5,1 " +
            "C 0.22,1 0,0.78 0,0.5 C 0,0.22 0.22,0 0.5,0 Z",
            Keywords: "ellipse scenario"),
        new(ShapeKind.UmlComponent, StencilCategory.Uml, "Component",
            "M 0.12,0 L 1,0 L 1,1 L 0.12,1 Z",
            "M 0,0.22 L 0.32,0.22 L 0.32,0.4 L 0,0.4 L 0,0.22 " +
            "M 0,0.6 L 0.32,0.6 L 0.32,0.78 L 0,0.78 L 0,0.6 " +
            "M 0.12,0 L 0.12,1",
            "module part"),
        new(ShapeKind.UmlNode, StencilCategory.Uml, "Node",
            "M 0,0.2 L 0.8,0.2 L 0.8,1 L 0,1 Z",
            "M 0,0.2 L 0.2,0 L 1,0 L 1,0.8 L 0.8,1 M 0.8,0.2 L 1,0",
            "device server box"),
        new(ShapeKind.UmlState, StencilCategory.Uml, "State",
            "M 0.18,0 L 0.82,0 C 0.94,0 1,0.1 1,0.22 L 1,0.78 C 1,0.9 0.94,1 0.82,1 " +
            "L 0.18,1 C 0.06,1 0,0.9 0,0.78 L 0,0.22 C 0,0.1 0.06,0 0.18,0 Z",
            Keywords: "rounded activity"),
        new(ShapeKind.UmlInitialState, StencilCategory.Uml, "Initial state",
            "M 0.5,0 C 0.78,0 1,0.22 1,0.5 C 1,0.78 0.78,1 0.5,1 " +
            "C 0.22,1 0,0.78 0,0.5 C 0,0.22 0.22,0 0.5,0 Z",
            Keywords: "start filled circle"),
        new(ShapeKind.UmlFinalState, StencilCategory.Uml, "Final state",
            "M 0.5,0 C 0.78,0 1,0.22 1,0.5 C 1,0.78 0.78,1 0.5,1 " +
            "C 0.22,1 0,0.78 0,0.5 C 0,0.22 0.22,0 0.5,0 Z",
            "M 0.5,0.18 C 0.68,0.18 0.82,0.32 0.82,0.5 C 0.82,0.68 0.68,0.82 0.5,0.82 " +
            "C 0.32,0.82 0.18,0.68 0.18,0.5 C 0.18,0.32 0.32,0.18 0.5,0.18 Z",
            "end stop"),

        // ---- Network --------------------------------------------------------------
        new(ShapeKind.Server, StencilCategory.Network, "Server",
            "M 0,0.14 L 0.78,0.14 L 0.78,1 L 0,1 Z",
            "M 0,0.14 L 0.22,0 L 1,0 L 1,0.86 L 0.78,1 " +
            "M 0.08,0.3 L 0.7,0.3 M 0.08,0.46 L 0.7,0.46 M 0.08,0.62 L 0.7,0.62",
            "host rack machine"),
        new(ShapeKind.Workstation, StencilCategory.Network, "Workstation",
            "M 0.04,0.06 L 0.96,0.06 L 0.96,0.66 L 0.04,0.66 Z",
            "M 0.14,0.16 L 0.86,0.16 L 0.86,0.56 L 0.14,0.56 L 0.14,0.16 " +
            "M 0.42,0.66 L 0.42,0.84 M 0.58,0.66 L 0.58,0.84 " +
            "M 0.22,0.84 L 0.78,0.84 L 0.86,0.96 L 0.14,0.96 Z",
            "pc desktop computer monitor"),
        new(ShapeKind.Laptop, StencilCategory.Network, "Laptop",
            "M 0.14,0.1 L 0.86,0.1 L 0.86,0.66 L 0.14,0.66 Z",
            "M 0.22,0.18 L 0.78,0.18 L 0.78,0.58 L 0.22,0.58 L 0.22,0.18 " +
            "M 0.02,0.78 L 0.98,0.78 L 0.9,0.66 L 0.1,0.66 Z",
            "notebook portable computer"),
        new(ShapeKind.Router, StencilCategory.Network, "Router",
            "M 0.5,0.16 C 0.86,0.16 1,0.34 1,0.5 C 1,0.66 0.86,0.84 0.5,0.84 " +
            "C 0.14,0.84 0,0.66 0,0.5 C 0,0.34 0.14,0.16 0.5,0.16 Z",
            "M 0.24,0.62 L 0.24,0.38 L 0.38,0.38 M 0.3,0.32 L 0.38,0.38 L 0.3,0.44 " +
            "M 0.76,0.38 L 0.76,0.62 L 0.62,0.62 M 0.7,0.56 L 0.62,0.62 L 0.7,0.68",
            "gateway network"),
        new(ShapeKind.Switch, StencilCategory.Network, "Switch",
            "M 0,0.34 L 0.8,0.34 L 1,0.16 L 1,0.66 L 0.8,0.84 L 0,0.84 Z",
            "M 0,0.34 L 0.2,0.16 L 1,0.16 M 0.8,0.34 L 0.8,0.84 " +
            "M 0.16,0.68 L 0.44,0.48 M 0.36,0.48 L 0.44,0.48 L 0.44,0.56 " +
            "M 0.64,0.5 L 0.36,0.7 M 0.44,0.7 L 0.36,0.7 L 0.36,0.62",
            "hub lan"),
        new(ShapeKind.Firewall, StencilCategory.Network, "Firewall",
            "M 0,0.1 L 1,0.1 L 1,0.9 L 0,0.9 Z",
            "M 0,0.36 L 1,0.36 M 0,0.64 L 1,0.64 " +
            "M 0.34,0.1 L 0.34,0.36 M 0.68,0.1 L 0.68,0.36 " +
            "M 0.17,0.36 L 0.17,0.64 M 0.51,0.36 L 0.51,0.64 M 0.85,0.36 L 0.85,0.64 " +
            "M 0.34,0.64 L 0.34,0.9 M 0.68,0.64 L 0.68,0.9",
            "security brick wall"),
        new(ShapeKind.Printer, StencilCategory.Network, "Printer",
            "M 0.04,0.34 L 0.96,0.34 L 0.96,0.74 L 0.04,0.74 Z",
            "M 0.2,0.34 L 0.2,0.08 L 0.8,0.08 L 0.8,0.34 " +
            "M 0.2,0.74 L 0.2,0.96 L 0.8,0.96 L 0.8,0.74 " +
            "M 0.78,0.44 L 0.88,0.44",
            "output paper"),
        new(ShapeKind.MobileDevice, StencilCategory.Network, "Mobile device",
            "M 0.3,0 L 0.7,0 C 0.76,0 0.78,0.03 0.78,0.08 L 0.78,0.92 " +
            "C 0.78,0.97 0.76,1 0.7,1 L 0.3,1 C 0.24,1 0.22,0.97 0.22,0.92 " +
            "L 0.22,0.08 C 0.22,0.03 0.24,0 0.3,0 Z",
            "M 0.28,0.12 L 0.72,0.12 L 0.72,0.82 L 0.28,0.82 L 0.28,0.12 " +
            "M 0.44,0.9 L 0.56,0.9",
            "phone tablet handheld"),
        new(ShapeKind.StorageArray, StencilCategory.Network, "Storage array",
            "M 0.06,0.12 L 0.94,0.12 L 0.94,0.88 L 0.06,0.88 Z",
            "M 0.06,0.37 L 0.94,0.37 M 0.06,0.63 L 0.94,0.63 " +
            "M 0.82,0.24 L 0.86,0.24 M 0.82,0.5 L 0.86,0.5 M 0.82,0.76 L 0.86,0.76",
            "nas san disk raid"),
        new(ShapeKind.WirelessAccessPoint, StencilCategory.Network, "Wireless access point",
            "M 0.3,0.66 L 0.7,0.66 L 0.78,0.92 L 0.22,0.92 Z",
            "M 0.5,0.66 L 0.5,0.4 " +
            "M 0.32,0.36 C 0.4,0.24 0.6,0.24 0.68,0.36 " +
            "M 0.2,0.22 C 0.34,0.02 0.66,0.02 0.8,0.22",
            "wifi antenna radio")
    ];

    /// <summary>
    /// Some shapes appear in two categories - a rectangle is also a flowchart Process - so
    /// the lookup keeps the first entry rather than refusing the duplicate.
    /// </summary>
    private static readonly Dictionary<ShapeKind, Stencil> ByKind = All
        .GroupBy(stencil => stencil.Kind)
        .ToDictionary(group => group.Key, group => group.First());

    public static Stencil? Find(ShapeKind kind) =>
        ByKind.TryGetValue(kind, out var stencil) ? stencil : null;

    public static IEnumerable<Stencil> InCategory(StencilCategory category) =>
        All.Where(stencil => stencil.Category == category);

    public static string CategoryName(StencilCategory category) => category switch
    {
        StencilCategory.Basic => "Basic shapes",
        StencilCategory.Arrow => "Arrows",
        StencilCategory.Callout => "Callouts",
        StencilCategory.Flowchart => "Flowchart",
        StencilCategory.Bpmn => "BPMN",
        StencilCategory.Uml => "UML",
        StencilCategory.Network => "Network",
        _ => category.ToString()
    };

    /// <summary>True when the stencil's name or keywords contain the search text.</summary>
    public static bool Matches(Stencil stencil, string search) =>
        string.IsNullOrWhiteSpace(search) ||
        stencil.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase) ||
        stencil.Keywords.Contains(search, System.StringComparison.OrdinalIgnoreCase);
}
