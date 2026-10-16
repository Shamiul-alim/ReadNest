using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ReadNest.Web.Components;

public static class AppRenderMode
{
    /// <summary>
    /// Interactive server rendering without prerendering. Pages load their data once, after the circuit starts,
    /// which avoids loading everything twice and the content flash that prerendering would cause.
    /// </summary>
    public static IComponentRenderMode Interactive { get; } = new InteractiveServerRenderMode(prerender: false);
}
