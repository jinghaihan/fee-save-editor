package catalog

import "testing"

func TestRecruitmentOrderCoversRoster(t *testing.T) {
	if len(RecruitmentOrder) != len(Persons) {
		t.Fatal("default order does not cover the complete playable roster")
	}
	seen := map[string]bool{}
	for _, id := range RecruitmentOrder {
		if Person(Hash(id)) == nil || seen[id] {
			t.Fatalf("unknown or duplicate default recruit: %s", id)
		}
		seen[id] = true
	}
	// Same-chapter and optional recruits retain the explicitly configured order.
	for _, pair := range [][2]string{{"PID_クロエ", "PID_ルイ"}, {"PID_ユナカ", "PID_ジャン"}, {"PID_シトリニカ", "PID_ラピス"}, {"PID_ゼルコバ", "PID_カゲツ"}, {"PID_ヴェイル", "PID_エル"}} {
		for i, id := range RecruitmentOrder[:len(RecruitmentOrder)-1] {
			if id == pair[0] && RecruitmentOrder[i+1] != pair[1] {
				t.Fatalf("default recruitment sequence changed: %v", pair)
			}
		}
	}
}
