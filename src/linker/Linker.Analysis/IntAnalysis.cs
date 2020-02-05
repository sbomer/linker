using System;

namespace Mono.Linker.Analysis {

    public class IntAnalysis {

        readonly IntCallGraph icg;
        public IntAnalysis(IntCallGraph icg) {
            this.icg = icg;
        }

        int[] reachesInteresting;
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