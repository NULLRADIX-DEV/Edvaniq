namespace Edvaniq.Client.DesignSystem;

// One answer of a ChoiceField or SelectField: what is sent, and what the user reads.
public sealed record ChoiceOption(string Value, string Text);