using System.Linq;

namespace Lapiro
{
    public partial class Footsteps : MVRScript
    {
        // Re-parse the four pipe-separated filter fields into their array form.
        // Called from the JSONStorableString callback (UpdateVarValue) whenever
        // the user edits one, and from OnEnable to refresh after scene load.
        public void updateVariables()
        {
            variableThisElements      = SplitPipes(variableThisElement.val);
            variableColliderGroups    = SplitPipes(variableColliderGroup.val);
            variableColliderIds       = SplitPipes(variableColliderId.val);
            variableColliderElements  = SplitPipes(variableColliderElement.val);
        }

        static string[] SplitPipes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Trim().Length == 0)
                return new string[0];
            return s.Split('|').Select(k => k.Trim()).ToArray();
        }

        public void UpdateVarValue(string s)
        {
            updateVariables();
        }

        // Slider callback. We don't react to live changes — the threshold values
        // are read directly from `treshold1.val` etc. on each collision — but
        // JSONStorableFloat requires a non-null callback.
        void BlendCallback(JSONStorableFloat jf)
        {
        }
    }
}
