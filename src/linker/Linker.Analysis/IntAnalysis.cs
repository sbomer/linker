using System;

namespace Mono.Linker.Analysis {

    public class IntAnalysis {

        readonly IntCallGraph icg;
        public IntAnalysis(IntCallGraph icg) {
            this.icg = icg;
            reachesInterestingWithoutGoingThroughLinkerSafe = new int[icg.numMethods];
        }

        int[] reachesInterestingWithoutGoingThroughLinkerSafe;
//         public bool ReachesInterestingWithoutGoingThroughLinkerSafe(int i) {
//             // memoize the result
//             if (reachesInterestingWithoutGoingThroughLinkerSafe[i] != 0) {
//                 if (reachesInteresting[i] == 1) {
//                     return true;
//                 }
//                 if (reachesInteresting[i] == -1) {
//                     return false;
//                 }
//                 // this happens when we are already in the process of computing the result.
//                 // temporarily answer false. may be incorrect in presence of cycles?
//                 return false;
//             }
// 
//             // base case. result depends only on this method.
//             if (icg.isLinkerSafe[i]) {
//                 return false;
//             }
//             if (icg.isInteresting[i]) {
//                 return true;
//             }
//             if (icg.calles[i] == null) {
//                 return false;
//             }
// 
//             // recursive case, where result depends on callees.
//             foreach (var j in icg.callees[i]) {
//                 if (ReachesInterestingWithoutGoingThroughLinkerSafe(j)) {
//                     return true;
//                 }
//             }
//             return false;
//         }

        int[] reachesInteresting;
//        // 0 means we haven't computed a result yet
        // // 1 means it reaches interesting
        // // -1 means it doesn't
        // // 2 means we have started computing the result ()
        // public bool ReachesInteresting(int i) {
        //     if (reachesInteresting[i] != 0) {
        //         // memoized result
        //         if (reachesInteresting[i] == 1) {
        //             return true;
        //         }
        //         if (reachesInteresting[i] == -1) {
        //             return false;
        //         }
        //         // TODO: I think this is a BUG! if evaluating along a cycle, will first indicate
        //         // it has started processing everything along the cycle.
        //         // when reaching the self node again, the direct caller will
        //         // see this return false. direct caller can then memoize the false result.
        //         // later when we get back to this method... we've got a problem.
        //         //System.Diagnostics.Debug.Assert(reachesInteresting[i] == 2);
        //         // conceptually what needs to happen when we encounter a cycle is,
        //         // the originator should go on to the next one.
        //         // but this one should wait on that result.
// 
        //         // a better approach: start with interesting, and bubble up. can be O(N^2).
        //         // we've already started computing the result for this one.
        //         // and have ended up here again (cycle).
        //         // in that case, just continue where we left off...
        //         // no, this doesn't really work.
        //     }
        //     // indicate we've started processing this one.
        //     reachesInteresting[i] = 2;
        //     if (icg.isInteresting[i]) {
        //         // interesting methods reach interesting by definition (base case)
        //         reachesInteresting[i] = 1;
        //         return true;
        //     }
        //     if (icg.callees[i] == null) {
        //         // no callees means it is not interesting
        //         reachesInteresting[i] = -1;
        //         return false;
        //     }
        //     // recurse into callees
        //     int calleeInd = 0;
        //     foreach (var j in icg.callees[i]) {
        //         reachesInteresting[i] = 2+calleeInd;
        //         if (ReachesInteresting(j)) {
        //             reachesInteresting[i] = 1;
        //             return true;
        //         }
        //         calleeInd++;
        //     }
        //     reachesInteresting[i] = -1;
        //     return false;
        // }

        public bool ReachesInteresting(int i) {
            if (reachesInteresting != null) {
                return reachesInteresting [i] == 1;
            }
            // just compute everything up-front

            // bubble up using a queue.
            int[] q = new int[icg.numMethods];
            int q_begin = 0;
            int q_end = 0;
            reachesInteresting = new int[icg.numMethods];
            for (int j = 0; j < icg.numMethods; j++) {
                if (icg.isInteresting [j]) {
                    reachesInteresting [j] = 1;
                    q[q_end] = j;
                    q_end++;
                }
            }

            while (q_end > q_begin) {
                // pop
                int j = q[q_begin];
                q_begin++;

                // look at neighbors
                if (icg.callers[j] == null)
                    continue;

                //foreach (int k in icg.callers[j]) {
                for (int ik = 0; ik < icg.callers[j].Length; ik++) {
                    int k = icg.callers[j][ik];

                    // don't re-queue an already interesting item
                    if (reachesInteresting [k] == 1)
                        continue;

                    reachesInteresting [k] = 1;
                    q[q_end] = k;
                    q_end++;
                }
            }

            // now return the answre
            return reachesInteresting [i] == 1;
        }
    }
}