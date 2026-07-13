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
            throw new DrunkDeerModelMismatchException(
                $"Connected keyboard is a {connection.Model.Name} (slug '{connection.Model.Slug}'), " +
                $"but KeyboardSession<{typeof(TModel).Name}> expects slug '{TModel.Slug}'. " +
                $"Use the untyped KeyboardSession.OpenFirst() or the marker type matching the connected model.");
    }

    /// <summary>
    /// Opens the first connected DrunkDeer keyboard and returns a typed session.
    /// Throws <see cref="DrunkDeerDeviceNotFoundException"/> if no compatible device is found,
    /// or <see cref="DrunkDeerModelMismatchException"/> if the connected keyboard's model
    /// doesn't match <typeparamref name="TModel"/>.
    /// </summary>
    public static new KeyboardSession<TModel> OpenFirst(ILoggerFactory? loggerFactory = null) =>
        new(KeyboardDiscoverer.OpenFirst(loggerFactory), loggerFactory);
}
