package app

import (
	"testing"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func TestDonationFieldsRemainVisibleWithoutEntries(t *testing.T) {
	a := fixture(t)
	a.cache["donations"] = cachedResult[[]save.Donation]{}
	tt := ui.NewTester(a.View, 1120, 780)
	for _, country := range catalog.DonationCountries {
		label := replaceFormat(a.T("CountryLevel"), country.Names.Name(a.Language))
		if _, ok := tt.AccessByRole(ui.RolePopUpButton, label); !ok {
			t.Errorf("missing donation field: %s", label)
		}
	}
}
