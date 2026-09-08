namespace VisualSidekick.Server;

public static class ModelConfiguration
{
    public const string EnvironmentVariable = "VISUAL_SIDEKICK_MODEL";
    public const string DefaultModel = "gpt-4.1";

    public static string Resolve(string? requestedModel)
    {
        var model = string.IsNullOrWhiteSpace(requestedModel)
            ? Environment.GetEnvironmentVariable(EnvironmentVariable)
            : requestedModel;

        return string.IsNullOrWhiteSpace(model)
            ? DefaultModel
            : model.Trim();
    }
}
