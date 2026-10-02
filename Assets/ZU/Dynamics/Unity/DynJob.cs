// The Burst job: one work item per character (its chains are solved together so the skirt ring can couple them), all
// characters of all ZuDynamics instances in one parallel-for. The batch is raw pointers into the manager's NativeArrays.
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ZU.Dynamics
{
    [BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard)]
    public unsafe struct DynJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public DynBatch B;

        public void Execute(int ci) => DynCore.SolveCharacter(ref B, ci);
    }
}
