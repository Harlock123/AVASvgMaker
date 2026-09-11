using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The data a shape carries, and the label that shows it.
/// </summary>
public class ShapeDataTests
{
    private static DiagramShape Carrying(DiagramDocument document, params (string Name, string Value)[] fields)
    {
        var shape = Harness.Box(document, new Rect(20, 20, 200, 100), "plain");

        foreach (var (name, value) in fields)
            shape.Fields.Add(new ShapeField { Name = name, Label = name, Value = value });

        return shape;
    }

    [AvaloniaFact]
    public void AShapeCarriesNoDataUntilItIsGivenSome()
    {
        var shape = Harness.Box(Harness.Page(400, 300), new Rect(20, 20, 100, 60), "plain");

        Assert.Empty(shape.Fields);
        Assert.Equal("plain", shape.DisplayText);
    }

    [AvaloniaFact]
    public void ALabelShowsTheFieldItNames()
    {
        var shape = Carrying(Harness.Page(400, 300), ("Owner", "Accounts"));
        shape.Text = "Owned by {Owner}";

        Assert.Equal("Owned by Accounts", shape.DisplayText);
    }

    [AvaloniaFact]
    public void ChangingTheValueChangesWhatTheLabelShows()
    {
        // The point of the whole thing: the label is not a copy of the answer, it is a
        // question the shape answers whenever it is asked.
        var shape = Carrying(Harness.Page(400, 300), ("Owner", "Accounts"));
        shape.Text = "{Owner}";

        shape.Fields[0].Value = "Payroll";

        Assert.Equal("Payroll", shape.DisplayText);
    }

    [AvaloniaFact]
    public void ALabelCanShowSeveralFieldsAndWordsBetweenThem()
    {
        var shape = Carrying(Harness.Page(400, 300), ("Host", "db01"), ("Role", "primary"));
        shape.Text = "{Host} - {Role}";

        Assert.Equal("db01 - primary", shape.DisplayText);
    }

    [AvaloniaFact]
    public void ANameThatMatchesNoFieldIsLeftExactlyAsTyped()
    {
        // There is nothing to escape, so a label that happens to contain braces is not
        // mangled for it.
        var shape = Carrying(Harness.Page(400, 300), ("Owner", "Accounts"));
        shape.Text = "see {note} and {Owner}";

        Assert.Equal("see {note} and Accounts", shape.DisplayText);
    }

    [AvaloniaFact]
    public void AnUnclosedBraceIsLeftAloneToo()
    {
        var shape = Carrying(Harness.Page(400, 300), ("Owner", "Accounts"));
        shape.Text = "{Owner and {Owner}";

        Assert.Equal("{Owner and Accounts", shape.DisplayText);
    }

    [AvaloniaFact]
    public void AFieldIsFoundWhateverCaseItIsNamedIn()
    {
        var shape = Carrying(Harness.Page(400, 300), ("Owner", "Accounts"));
        shape.Text = "{owner}";

        Assert.Equal("Accounts", shape.DisplayText);
    }

    [AvaloniaFact]
    public void AnEmptyFieldShowsAsNothingRatherThanAsItsName()
    {
        var shape = Carrying(Harness.Page(400, 300), ("Owner", ""));
        shape.Text = "[{Owner}]";

        Assert.Equal("[]", shape.DisplayText);
    }

    [AvaloniaFact]
    public void WhatIsDrawnIsWhatTheFieldsSay()
    {
        // Not just the property: the words that actually reach the page and the SVG.
        var document = Harness.Page(400, 300);
        var shape = Carrying(document, ("Owner", "Accounts"));
        shape.Text = "{Owner}";

        Assert.Contains("Accounts", shape.ToSvg());
        Assert.DoesNotContain("{Owner}", shape.ToSvg());
    }

    [AvaloniaFact]
    public void DataSurvivesSavingAndOpening()
    {
        var document = Harness.Page(400, 300);
        var shape = Carrying(document, ("Owner", "Accounts"), ("Cost", "1200"));

        shape.Fields[1].Label = "Annual cost";

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document)).Pages[0].Shapes[0];

        Assert.Equal(2, back.Fields.Count);
        Assert.Equal("Owner", back.Fields[0].Name);
        Assert.Equal("Accounts", back.Fields[0].Value);
        Assert.Equal("Annual cost", back.Fields[1].Caption);
        Assert.Equal("1200", back.Fields[1].Value);
    }

    [AvaloniaFact]
    public void AShapeWithNoDataWritesNoneOfIt()
    {
        var document = Harness.Page(400, 300);
        Harness.Box(document, new Rect(10, 10, 40, 40), "plain");

        Assert.DoesNotContain("fields", DiagramFile.ToJson(document));
    }

    [AvaloniaFact]
    public void AFieldLabelledTheSameAsItsNameIsNotWrittenTwice()
    {
        var document = Harness.Page(400, 300);
        Carrying(document, ("Owner", "Accounts"));

        var json = DiagramFile.ToJson(document);

        Assert.Contains("\"name\": \"Owner\"", json);
        Assert.DoesNotContain("\"label\"", json);
    }

    [AvaloniaFact]
    public void AFileFromBeforeShapesCarriedDataStillOpens()
    {
        // Version 16 had nowhere to put it.
        var document = Harness.Page(400, 300);
        Carrying(document, ("Owner", "Accounts"));

        var json = DiagramFile.ToJson(document)
            .Replace($"\"version\": {DiagramFile.CurrentVersion}", "\"version\": 16");

        var back = DiagramFile.FromJson(json).Pages[0].Shapes[0];

        Assert.Equal("plain", back.Text);
    }

    [AvaloniaFact]
    public void TheCopiedFieldsOfAShapeAreItsOwn()
    {
        // The dialog edits copies so that cancelling leaves the shape as it was found.
        var field = new ShapeField { Name = "Owner", Value = "Accounts" };
        var copy = field.Copy();

        copy.Value = "Payroll";

        Assert.Equal("Accounts", field.Value);
    }
}
