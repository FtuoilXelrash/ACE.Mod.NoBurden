namespace NoBurden;

public class Mod : BasicMod
{
    public Mod() : base() => Setup(nameof(NoBurden), new PatchClass(this));
}
