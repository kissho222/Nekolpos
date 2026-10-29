// Minimal negative control: the pre-fix batch predicate ignored automatedStatus and phase.
public static class LegacyBatch
{
    public static bool ShouldContinue(bool normalSavesPreserved, bool scenePreserved)
    {
        if (!normalSavesPreserved || !scenePreserved) return false;
        return true;
    }
}
