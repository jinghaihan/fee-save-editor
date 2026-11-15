package catalog

import (
	"strings"
	"testing"
)

func TestChapterRecordNames(t *testing.T) {
	for _, l := range Languages {
		for _, c := range chapterRecordData.Chapters {
			if c.Names[l.Code] == "" || ChapterName(c.ID, l.Code) == c.ID {
				t.Fatalf("missing %s %s", c.ID, l.Code)
			}
		}
		for _, id := range []string{"M000_リュール", "パネトネ"} {
			if RecordPersonName(id, l.Code) == "" || l.Code != "ja" && RecordPersonName(id, l.Code) == id {
				t.Fatalf("unknown MVP %s %s", id, l.Code)
			}
		}
		for _, id := range []string{"M000_神竜ノ子", "ウォーリアー"} {
			if RecordClassName(id, l.Code) == "" || l.Code != "ja" && RecordClassName(id, l.Code) == id {
				t.Fatalf("unknown class %s %s", id, l.Code)
			}
		}
		for _, id := range []string{"M000_マルス", "ディミトリ", "クロード", "アイク", "エフラム", "ルフレ"} {
			if RecordEmblemName(id, l.Code) == "" || l.Code != "ja" && RecordEmblemName(id, l.Code) == id {
				t.Fatalf("unknown emblem %s %s", id, l.Code)
			}
		}
	}
	if !strings.Contains(ChapterName("CID_M014", "zh-Hans"), "索尔姆") || !strings.Contains(ChapterName("CID_M014", "en"), "Solm") {
		t.Fatal("incorrect chapter name")
	}
	if RecordEmblemName("unknown", "en") != "unknown" {
		t.Fatal("unknown name not preserved")
	}
}
