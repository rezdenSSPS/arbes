using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static AirsoftFPS.UIKit;

namespace AirsoftFPS
{
    // Lobby part of the Medic mode: a "volunteer as medic" toggle under the bot options and a
    // MEDIC badge on the player slots of volunteers (only while the Medic mode is picked).
    public partial class NetworkUI
    {
        GameObject medicRow;
        Segmented medicSeg;
        readonly List<GameObject> medicBadges = new List<GameObject>();

        // y = top of the free strip under the bot note in the player column.
        void BuildMedicOptions(RectTransform colRt, float y)
        {
            var row = Rect(colRt, "MedicVolunteer").TL(0, y, 556, 32);
            var cap = HRow(row, "Caption", 8, TextAnchor.MiddleLeft);
            cap.GetComponent<RectTransform>().Fill();
            var cross = Box(cap.transform, "Cross", Medic.MedicWhite);
            cross.LE(14, 14);
            Box(cross.transform, "V", Medic.MedicRed).rectTransform.At(0.5f, 0.5f, 0, 0, 4, 10);
            Box(cross.transform, "H", Medic.MedicRed).rectTransform.At(0.5f, 0.5f, 0, 0, 10, 4);
            Caption(cap.transform, "MEDIC  ·  1 PER 4 PLAYERS", 12, Muted, 0.16f);
            medicSeg = Segments(row, new[] { "NO", "VOLUNTEER" }, Medic.LocalVolunteer ? 1 : 0, 28, 12,
                i => Medic.SetLocalVolunteer(i == 1), 12f);
            var segRt = (RectTransform)medicSeg.transform;
            segRt.anchorMin = segRt.anchorMax = segRt.pivot = new Vector2(1f, 0.5f);
            segRt.anchoredPosition = Vector2.zero;
            medicRow = row.gameObject;
            medicRow.SetActive(false);

            foreach (var s in slots)
            {
                var b = Badge(s.host.transform.parent, "MEDIC", Medic.MedicWhite, Medic.MedicRed, Color.clear).gameObject;
                b.SetActive(false);
                medicBadges.Add(b);
            }
        }

        void RefreshMedicOptions(MatchNet mnet)
        {
            if (medicRow == null) return;
            bool medic = mnet.Mode == MatchNet.GameModeType.Medic;
            medicRow.SetActive(medic);
            int want = Medic.LocalVolunteer ? 1 : 0;
            if (medicSeg.Selected != want) medicSeg.Select(want, false);
            var roster = mnet.Roster;
            for (int i = 0; i < medicBadges.Count; i++)
                medicBadges[i].SetActive(medic && i < roster.Count && Medic.IsVolunteer(roster[i].clientId));
        }
    }
}
