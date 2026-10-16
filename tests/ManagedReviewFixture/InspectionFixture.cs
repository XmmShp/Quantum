namespace ManagedReviewFixture;

public static class InspectionFixture
{
    static InspectionFixture() => throw new System.InvalidOperationException("Plugin code must never execute during inspection");
    public static string Destination() => "https://inspection-fixture.invalid/visible-in-il";
}
