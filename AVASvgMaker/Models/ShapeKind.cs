namespace AVASvgMaker.Models;

/// <summary>
/// Every shape the catalogue offers. The name is what gets written to a file, so entries are
/// added rather than renamed or reordered.
/// </summary>
public enum ShapeKind
{
    // Basic
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Diamond,
    Triangle,
    Hexagon,
    Parallelogram,
    Cylinder,

    // Placed from the toolbar rather than the stencil toolbox.
    TextBox,
    Connector,

    // Basic, continued
    RightTriangle,
    Trapezoid,
    Pentagon,
    Octagon,
    Star,
    Cross,
    BlockArrow,
    Chevron,
    Cloud,

    // Flowchart
    PredefinedProcess,
    ManualInput,
    ManualOperation,
    Document,
    MultiDocument,
    OffPageReference,
    OnPageConnector,
    Delay,
    StoredData,
    InternalStorage,
    Merge,
    Extract,
    SummingJunction,
    Or,
    Display,
    Card,
    Collate,
    Sort,

    // Containers
    Pool,
    Lane,
    ContainerBox,

    // BPMN
    BpmnStartEvent,
    BpmnIntermediateEvent,
    BpmnEndEvent,
    BpmnMessageEvent,
    BpmnTimerEvent,
    BpmnTask,
    BpmnSubprocess,
    BpmnUserTask,
    BpmnServiceTask,
    BpmnScriptTask,
    BpmnExclusiveGateway,
    BpmnParallelGateway,
    BpmnInclusiveGateway,
    BpmnEventGateway,
    BpmnDataObject,
    BpmnGroup,

    // UML
    UmlClass,
    UmlInterface,
    UmlPackage,
    UmlNote,
    UmlActor,
    UmlUseCase,
    UmlComponent,
    UmlNode,
    UmlState,
    UmlInitialState,
    UmlFinalState,

    // Network
    // Arrows
    ArrowLeft,
    ArrowUp,
    ArrowDown,
    DoubleArrow,
    DoubleArrowVertical,
    BentArrow,
    CurvedArrow,
    NotchedArrow,

    // Callouts
    SpeechBubble,
    OvalCallout,
    ThoughtBubble,
    RectangularCallout,
    AnnotationBracket,

    // Network
    Server,
    Workstation,
    Laptop,
    Router,
    Switch,
    Firewall,
    Printer,
    MobileDevice,
    StorageArray,
    WirelessAccessPoint,

    // Electrical. Mostly line work rather than bodies - see StencilCatalogue.
    Resistor,
    Capacitor,
    PolarisedCapacitor,
    Inductor,
    Diode,
    LightEmittingDiode,
    ZenerDiode,
    Battery,
    DcSource,
    AcSource,
    Ground,
    Chassis,
    SwitchContact,
    Fuse,
    Lamp,
    Transformer,
    Transistor,
    Amplifier,
    Antenna,
    Motor,
    DigitalGround,
    Rail5V,
    Rail3V3,
    SupplyRail,
    NegativeRail,
    SupplyFlag,
    NoConnect,

    // Electronic. Packages with a leg per pin - see ChipCatalogue and ChipShape.
    Chip555,
    Chip741,
    Chip7400,
    Chip7402,
    Chip7404,
    Chip7408,
    Chip7432,
    Chip7474,
    Chip7486,
    Chip74138,
    Chip74595,
    Dip8,
    Dip14,
    Dip16,

    /// <summary>An outline of its own, rather than one from the catalogue. Imported SVG
    /// arrives as these where nothing simpler fits.</summary>
    Path
}
