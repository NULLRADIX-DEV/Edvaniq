using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Content.Domain;

// When two topic names of one analysis mean the same topic: by the rule Knowledge merges topics by (TopicName), so a
// document never brings two topics that Knowledge would take for one.
public static class TopicKey
{
    public static string Of(string name) => TopicName.Normalize(name);
}