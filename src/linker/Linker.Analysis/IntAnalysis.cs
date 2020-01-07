using System;

namespace Mono.Linker.Analysis {

    public class IntAnalysis {

        readonly IntCallGraph icg;
        public IntAnalysis(IntCallGraph icg) {
            this.icg = icg;
            reachesInteresting = new int[icg.numMethods];
        }

        int[] reachesInteresting;
        // 0 means we haven't computed a result yet
        // 1 means it reaches interesting
        // -1 means it doesn't
        // 2 means we have started computing the result
        public bool ReachesInteresting(int i) {
            if (reachesInteresting[i] != 0) {
                // memoized result
                if (reachesInteresting[i] == 1) {
                    return true;
                }
                if (reachesInteresting[i] == -1) {
                    return false;
                }
                System.Diagnostics.Debug.Assert(reachesInteresting[i] == 2);
                return false;
            }
            // indicate we've started processing this one.
            reachesInteresting[i] = 2;
            if (icg.isInteresting[i]) {
                // interesting methods reach interesting by definition (base case)
                reachesInteresting[i] = 1;
                return true;
            }
            if (icg.callees[i] == null) {
                // no callees means it is not interesting
                reachesInteresting[i] = -1;
                return false;
            }
            // recurse into callees
            foreach (var j in icg.callees[i]) {
                if (ReachesInteresting(j)) {
                    reachesInteresting[i] = 1;
                    return true;
                }
            }
            reachesInteresting[i] = -1;
            return false;
        }
    }
}