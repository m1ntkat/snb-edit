using SunabaSDK.BspEditor.Commands;
using SunabaSDK.BspEditor.Documents;
using SunabaSDK.BspEditor.Editing.Components.Visgroup;
using SunabaSDK.BspEditor.Modification;
using SunabaSDK.BspEditor.Modification.Operations.Data;
using SunabaSDK.BspEditor.Primitives.MapData;
using SunabaSDK.Common.Shell.Commands;
using SunabaSDK.Common.Translations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SunabaSDK.BspEditor.Editing.Commands
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Map:Visgroups")]
    public class EditVisgroups : BaseCommand
    {
        [Import] private Lazy<ITranslationStringProvider> _translator;

        public override string Name { get; set; } = "Edit visgroups";
        public override string Details { get; set; } = "View and edit map visgroups";

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            using (var vg = new VisgroupEditForm(document))
            {
                _translator.Value.Translate(vg);
                if (vg.ShowDialog() == DialogResult.OK)
                {
                    var nv = new List<Visgroup>();
                    var cv = new List<Visgroup>();
                    var dv = new List<Visgroup>();

                    vg.PopulateChangeLists(document, nv, cv, dv);

                    if (nv.Any() || cv.Any() || dv.Any())
                    {
                        var tns = new Transaction();

                        if (dv.Any())
                        {
                            var ids = dv.Select(x => x.ID).ToList();
                            tns.Add(new RemoveMapData(document.Map.Data.Get<Visgroup>().Where(x => ids.Contains(x.ID))));
                        }

                        if (cv.Any())
                        {
                            var ids = cv.Select(x => x.ID).ToList();
                            tns.Add(new RemoveMapData(document.Map.Data.Get<Visgroup>().Where(x => ids.Contains(x.ID))));
                            tns.Add(new AddMapData(cv));
                        }

                        if (nv.Any())
                        {
                            tns.Add(new AddMapData(nv));
                        }

                        await MapDocumentOperation.Perform(document, tns);
                    }
                }
            }
        }
    }
}