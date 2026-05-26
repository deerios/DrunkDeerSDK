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
{
    internal KeyboardSession(IKeyboardConnection connection) : base(connection) { }

    /// <summary>
    /// Opens the first connected DrunkDeer keyboard and returns a typed session.
    /// Throws <see cref="DrunkDeerDeviceNotFoundException"/> if no compatible device is found.
    /// </summary>
    public static new KeyboardSession<TModel> OpenFirst() =>
        new(KeyboardDiscoverer.OpenFirst());
}
