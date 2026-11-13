package catalog

import (
	"compress/gzip"
	"embed"
	"encoding/json"
	"fmt"
	"io"
	"unicode/utf16"
)

//go:embed data/*.json.gz
var data embed.FS

func Decode(name string, target any) error {
	f, e := data.Open("data/" + name + ".json.gz")
	if e != nil {
		return e
	}
	defer f.Close()
	z, e := gzip.NewReader(f)
	if e != nil {
		return e
	}
	defer z.Close()
	return json.NewDecoder(z).Decode(target)
}
func Raw(name string) []byte {
	f, e := data.Open("data/" + name + ".json.gz")
	if e != nil {
		panic(e)
	}
	defer f.Close()
	z, e := gzip.NewReader(f)
	if e != nil {
		panic(e)
	}
	defer z.Close()
	b, e := io.ReadAll(z)
	if e != nil {
		panic(e)
	}
	return b
}
func Hash(id string) uint32 {
	h := uint32(2166136261)
	for _, u := range utf16.Encode([]rune(id)) {
		h = h*16777619 ^ uint32(u)
	}
	return h
}

var Languages = []struct{ Code, NativeName string }{{"en", "English"}, {"zh-Hans", "简体中文"}, {"zh-Hant", "繁體中文"}, {"ja", "日本語"}, {"ko", "한국어"}, {"de", "Deutsch"}, {"fr", "Français"}, {"es", "Español"}, {"it", "Italiano"}}

type ItemDefinition struct {
	Id, English, Chinese     string
	Kind, MaxUses, MaxRefine int
	Names                    map[string]string
}

func (i ItemDefinition) Hash() uint32 { return Hash(i.Id) }
func (i ItemDefinition) Name(language string) string {
	if n := i.Names[language]; n != "" {
		return n
	}
	return i.English
}

var Items = func() []ItemDefinition {
	var data struct{ Items []ItemDefinition }
	if e := Decode("items", &data); e != nil {
		panic(e)
	}
	return data.Items
}()
var itemsByHash = func() map[uint32]ItemDefinition {
	m := map[uint32]ItemDefinition{}
	for _, i := range Items {
		m[i.Hash()] = i
	}
	return m
}()

func Item(hash uint32) *ItemDefinition {
	if i, ok := itemsByHash[hash]; ok {
		return &i
	}
	return nil
}
func ItemByID(id string) (ItemDefinition, error) {
	if i := Item(Hash(id)); i != nil && i.Id == id {
		return *i, nil
	}
	return ItemDefinition{}, fmt.Errorf("unknown or unsupported convoy item: %s", id)
}
