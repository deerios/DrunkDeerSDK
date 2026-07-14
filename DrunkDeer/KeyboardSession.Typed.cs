using Microsoft.Extensions.Logging;

namespace DrunkDeer.Protocol;

/// <summary>
/// A <see cref="KeyboardSession"/> bound to a specific model type at compile time.
/// The type parameter <typeparamref name="TModel"/> is a phantom type (model marker class
/// generated from the protocol YAML) whose implemented interfaces determine which
/// capability-gated extension methods appear in intellisense.
/// </summary>
/// <typeparam name="TModel">
/// A model marker type such as <see cref="A75Ultra"/>, <see cref="G65M1"/>, or <see cref="G60"/>.
/// Models that support the FuncBlock gateway implement <see cref="IHasFuncBlock"/> (or a
/// sub-interface thereof), which unlocks the corresponding extension methods.
/// </typeparam>
public sealed class KeyboardSession<TModel> : KeyboardSession
    where TModel : IModelMarker
{
    internal KeyboardSession(IKeyboardConnection connection, ILoggerFactory? loggerFactory = null)
        : base(connection, loggerFactory)
    {
        // The marker type only gates which extension methods intellisense shows - it does not
        // verify the connected hardware. Without this check, KeyboardSession<A75Ultra>.OpenFirst()
        // with a plain A75 connected would compile and "work" until an A75Ultra-only method threw
        // NotSupportedException (or worse, silently misbehaved) at some arbitrary later call.
        if (connection.Model.Slug != TModel.Slug)
        {
            // Base construction succeeded and took ownership of the just-opened connection.
            // Dispose it before rejecting the mismatch, otherwise the hidraw streams leak and
            // the keyboard reports "in use by another process" to the next opener until GC.
            connection.Dispose();
            throw new DrunkDeerModelMismatchException(
                $"Connected keyboard is a {connection.Model.Name} (slug '{connection.Model.Slug}'), " +
                $"but KeyboardSession<{typeof(TModel).Name}> expects slug '{TModel.Slug}'. " +
                $"Use the untyped KeyboardSession.OpenFirst() or the marker type matching the connected model.");
        }
    }

    /// <summary>
    /// Opens the first connected DrunkDeer keyboard whose model matches <typeparamref name="TModel"/>
    /// and returns a typed session. With several keyboards attached this scans past ones of other
    /// models instead of binding whichever completes the handshake first.
    /// Throws <see cref="DrunkDeerModelMismatchException"/> if a device was found but none matched
    /// <typeparamref name="TModel"/> (the message lists what was found), or
    /// <see cref="DrunkDeerDeviceNotFoundException"/> if no DrunkDeer keyboard is present at all.
    /// </summary>
    public static new KeyboardSession<TModel> OpenFirst(ILoggerFactory? loggerFactory = null)
    {
        var rejected = new List<string>();
        foreach (var connection in KeyboardDiscoverer.OpenAll(loggerFactory))
        {
            if (connection.Model.Slug == TModel.Slug)
                return new KeyboardSession<TModel>(connection, loggerFactory);

            // Handshook, but it's the wrong model for this typed session. Dispose it so the
            // streams aren't leaked, note what it was, and keep scanning the remaining candidates.
            rejected.Add($"{connection.Model.Name} (slug '{connection.Model.Slug}')");
            connection.Dispose();
        }

        throw new DrunkDeerModelMismatchException(rejected.Count == 0
            ? $"No DrunkDeer keyboard completed the identity handshake, so none could be matched " +
              $"to KeyboardSession<{typeof(TModel).Name}> (slug '{TModel.Slug}')."
            : $"No connected keyboard matches KeyboardSession<{typeof(TModel).Name}> (slug '{TModel.Slug}'). " +
              $"Found: {string.Join(", ", rejected)}.");
    }
}
