package catalog

import "strings"

type ChapterDefinition struct {
	ID              string
	Names, Prefixes Names
}

func (d ChapterDefinition) Name(language string) string {
	title := d.Names.Name(language)
	if prefix := d.Prefixes.Name(language); prefix != "" {
		return prefix + " · " + title
	}
	return title
}

type RecordEmblemDefinition struct {
	ID    string
	Names Names
}

var chapterRecordData struct {
	Chapters []ChapterDefinition
	Emblems  []RecordEmblemDefinition
}

func init() {
	if err := Decode("chapter-records", &chapterRecordData); err != nil {
		panic(err)
	}
}

func ChapterName(id, language string) string {
	for _, d := range chapterRecordData.Chapters {
		if d.ID == id {
			return d.Name(language)
		}
	}
	return id
}

func historicalID(id string) string { return strings.TrimPrefix(id, "M000_") }
func RecordPersonName(id, language string) string {
	for _, p := range Persons {
		if p.Id == "PID_"+historicalID(id) {
			return p.Name(language)
		}
	}
	return id
}
func RecordClassName(id, language string) string {
	for _, p := range Classes {
		if p.Id == "JID_"+historicalID(id) {
			return p.Name(language)
		}
	}
	return id
}
func RecordEmblemName(id, language string) string {
	if p := Emblem("GID_" + historicalID(id)); p != nil {
		return p.Name(language)
	}
	for _, p := range chapterRecordData.Emblems {
		if p.ID == historicalID(id) {
			return p.Names.Name(language)
		}
	}
	return id
}
func RecordEmblems() []RecordEmblemDefinition {
	out := make([]RecordEmblemDefinition, 0, len(Emblems)+len(chapterRecordData.Emblems))
	for _, e := range Emblems {
		out = append(out, RecordEmblemDefinition{strings.TrimPrefix(e.Id, "GID_"), e.Names})
	}
	return append(out, chapterRecordData.Emblems...)
}
