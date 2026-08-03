using BDArmory.Utils;

namespace BDArmory
{
    public class BDAPartModule : PartModule
    {
        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);
            this.SetDefaultChooseOptionHandlers();
            this.SetDefaultToggleHanders();
        }
    }
}