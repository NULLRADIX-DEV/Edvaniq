using Bunit;

using Microsoft.AspNetCore.Components;

namespace Edvaniq.Client.UnitTests;

// Since #278 nothing goes up without the confirmation, so a test that pastes or uploads ticks it first.
internal static class MaterialConfirm
{
    public static void Confirm<T>(this IRenderedComponent<T> page)
        where T : IComponent =>
        page.Find("#material-confirm").Change(true);
}