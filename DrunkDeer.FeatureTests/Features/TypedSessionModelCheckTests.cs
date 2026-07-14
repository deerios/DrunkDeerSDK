using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// API-4 regression coverage: <see cref="KeyboardSession{TModel}"/> must verify the connected
/// model matches <c>TModel</c> instead of silently branding whatever's plugged in.
/// </summary>
[TestFixture]
public class TypedSessionModelCheckTests
{
	[Test]
	public void Ctor_ModelMismatch_Throws()
	{
		// TModel = A75Ultra, but the fake advertises a plain A75.
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75));
		var ex = Assert.Throws<DrunkDeerModelMismatchException>(() => new KeyboardSession<A75Ultra>(fake));
		Assert.That(ex!.Message, Does.Contain("A75").And.Contain("A75Ultra"));
		// The typed ctor took ownership of the connection OpenFirst opened, so a mismatch must
		// dispose it rather than leak the hidraw streams (PLAN-BUG-1).
		Assert.That(fake.DisposeCount, Is.EqualTo(1));
	}

	[Test]
	public void Ctor_ModelMatch_DoesNotThrow()
	{
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75Ultra));
		using var session = new KeyboardSession<A75Ultra>(fake);
		Assert.That(session, Is.Not.Null);
	}
}
