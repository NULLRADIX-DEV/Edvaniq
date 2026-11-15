namespace Edvaniq.Services.Content.Domain;

// Where the content of a document comes from (#349). Material is what the user pasted or uploaded, everything made from
// it has a source. Synthetic is a text the AI wrote without material (MF035, M5), what is made from it has none. The
// number is stored, so it never changes.
public enum DocumentOrigin
{
    Material = 0,
    Synthetic = 1,

    // The demo material of the demo learning goal (#411). It has a source like material, and the worker answers its AI
    // calls from the answers that ship with it, so it costs nothing and needs no provider.
    Demo = 2,
}