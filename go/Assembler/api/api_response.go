package api

import (
	"assembler/model"
	"fmt"
	"strings"
)

type TemplateData struct {
	Html string
	Json string
}

type PreProcessTemplateMetadata struct {
	OriginalContent        string
	Placeholders           []model.TemplatePlaceholder
	SlottedTemplates       []model.SlottedTemplate
	JsonData               *map[string]interface{}
	JsonPlaceholders       []model.JsonPlaceholder
	ReplacementMappings    []model.ReplacementMapping
	HasPlaceholders        bool
	HasSlottedTemplates    bool
	HasJsonData            bool
	HasJsonPlaceholders    bool
	HasReplacementMappings bool
	RequiresProcessing     bool
}

type ApiResponse struct {
	Templates           map[string]TemplateData
	PreProcessTemplates map[string]PreProcessTemplateMetadata
	AppSite             string
	AppFile             string
	AppView             string
	ServerTimeMs        float64
	Html                string
	EngineTimeMs        float64
}

func (ar ApiResponse) SerializeToJson(indented bool) string {
	if indented {
		return ar.serializeToJsonPretty()
	}
	return ar.serializeToJsonCompact()
}

func (ar ApiResponse) serializeToJsonCompact() string {
	var sb strings.Builder
	sb.WriteString("{")
	sb.WriteString("\"Templates\":")
	sb.WriteString(ar.serializeTemplates(0, false))
	sb.WriteString(",\"PreProcessTemplates\":")
	sb.WriteString(ar.serializePreProcessTemplates(0, false))
	sb.WriteString(",\"AppSite\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ar.AppSite))
	sb.WriteString("\"")
	if ar.AppFile != "" {
		sb.WriteString(",\"AppFile\":\"")
		sb.WriteString(ApiResponse_EscapeJsonString(ar.AppFile))
		sb.WriteString("\"")
	}
	if ar.AppView != "" {
		sb.WriteString(",\"AppView\":\"")
		sb.WriteString(ApiResponse_EscapeJsonString(ar.AppView))
		sb.WriteString("\"")
	}
	sb.WriteString(fmt.Sprintf(",\"ServerTimeMs\":%f", ar.ServerTimeMs))
	sb.WriteString(",\"Html\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(ar.Html))
	sb.WriteString("\"")
	sb.WriteString(fmt.Sprintf(",\"EngineTimeMs\":%f", ar.EngineTimeMs))
	sb.WriteString("}")
	jsonStr := sb.String()
	jsonStr = strings.Replace(jsonStr, ",}", "}", -1)
	jsonStr = strings.Replace(jsonStr, ",]", "]", -1)
	return jsonStr
}

func (ar ApiResponse) serializeToJsonPretty() string {
	var sb strings.Builder
	sb.WriteString("{\n  ")
	sb.WriteString("\"Templates\": ")
	sb.WriteString(ar.serializeTemplates(1, true))
	sb.WriteString(",\n  ")
	sb.WriteString("\"PreProcessTemplates\": ")
	sb.WriteString(ar.serializePreProcessTemplates(1, true))
	sb.WriteString(",\n  ")
	sb.WriteString("\"AppSite\": \"")
	sb.WriteString(ApiResponse_EscapeJsonString(ar.AppSite))
	sb.WriteString("\"")
	if ar.AppFile != "" {
		sb.WriteString(",\n  ")
		sb.WriteString("\"AppFile\": \"")
		sb.WriteString(ApiResponse_EscapeJsonString(ar.AppFile))
		sb.WriteString("\"")
	}
	if ar.AppView != "" {
		sb.WriteString(",\n  ")
		sb.WriteString("\"AppView\": \"")
		sb.WriteString(ApiResponse_EscapeJsonString(ar.AppView))
		sb.WriteString("\"")
	}
	sb.WriteString(",\n  ")
	sb.WriteString(fmt.Sprintf("\"ServerTimeMs\": %f", ar.ServerTimeMs))
	sb.WriteString(",\n  ")
	sb.WriteString("\"Html\": \"")
	sb.WriteString(ApiResponse_EscapeHtmlString(ar.Html))
	sb.WriteString("\"")
	sb.WriteString(",\n  ")
	sb.WriteString(fmt.Sprintf("\"EngineTimeMs\": %f", ar.EngineTimeMs))
	sb.WriteString("\n}")
	return sb.String()
}

func ApiResponse_EscapeJsonString(input string) string {
	return strings.NewReplacer(
		"\\", "\\\\",
		"\"", "\\\"",
		"\n", "\\n",
		"\r", "\\r",
		"\t", "\\t",
		"<", "\\u003C",
		">", "\\u003E",
		"&", "\\u0026",
		"'", "\\u0027",
		"+", "\\u002B",
	).Replace(input)
}

func ApiResponse_EscapeHtmlString(input string) string {
	return strings.NewReplacer(
		"\\", "\\\\",
		"\"", "\\\"",
		"\n", "\\n",
		"\r", "\\r",
		"\t", "\\t",
	).Replace(input)
}

func (ar ApiResponse) serializeTemplates(indent int, indented bool) string {
	var sb strings.Builder
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
	}
	first := true
	for k, v := range ar.Templates {
		if !first {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(k))
		sb.WriteString("\":")
		if indented {
			sb.WriteString(" ")
		}
		sb.WriteString(ar.serializeTemplateData(v, indent+1, indented))
		first = false
	}
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) serializeTemplateData(td TemplateData, indent int, indented bool) string {
	var sb strings.Builder
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Html\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(td.Html))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Json\":")
	if indented {
		sb.WriteString(" ")
	}
	if td.Json != "" {
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(td.Json))
		sb.WriteString("\"")
	} else {
		sb.WriteString("null")
	}
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) serializePreProcessTemplates(indent int, indented bool) string {
	var sb strings.Builder
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
	}
	first := true
	for k, v := range ar.PreProcessTemplates {
		if !first {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(k))
		sb.WriteString("\":")
		if indented {
			sb.WriteString(" ")
		}
		sb.WriteString(ar.serializePreProcessTemplateMetadata(v, indent+1, indented))
		first = false
	}
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) serializePreProcessTemplateMetadata(pm PreProcessTemplateMetadata, indent int, indented bool) string {
	var sb strings.Builder
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"OriginalContent\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(pm.OriginalContent))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Placeholders\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializePlaceholdersList(&sb, pm.Placeholders, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"SlottedTemplates\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeSlottedTemplatesList(&sb, pm.SlottedTemplates, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"JsonData\":")
	if indented {
		sb.WriteString(" ")
	}
	// Manually serialize JsonData map to proper JSON format (handles booleans correctly)
	sb.WriteString(serializeJsonDataMap(pm.JsonData))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"JsonPlaceholders\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeJsonPlaceholdersList(&sb, pm.JsonPlaceholders, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"ReplacementMappings\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeReplacementMappingsList(&sb, pm.ReplacementMappings, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasPlaceholders\": %t", pm.HasPlaceholders))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasSlottedTemplates\": %t", pm.HasSlottedTemplates))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasJsonData\": %t", pm.HasJsonData))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasJsonPlaceholders\": %t", pm.HasJsonPlaceholders))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasReplacementMappings\": %t", pm.HasReplacementMappings))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"RequiresProcessing\": %t", pm.RequiresProcessing))
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) SerializeSlotPlaceholdersList(sb *strings.Builder, list []model.SlotPlaceholder, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("[")
	if indented && len(list) > 0 {
		sb.WriteString("\n")
	}
	for i, slot := range list {
		if i > 0 {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		ar.SerializeSlotPlaceholder(sb, slot, indent, indented)
	}
	if indented && len(list) > 0 {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeSlotPlaceholder(sb *strings.Builder, slot model.SlotPlaceholder, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Number\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Number))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"StartIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", slot.StartIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"EndIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", slot.EndIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Content\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Content))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"SlotKey\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.SlotKey))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"OpenTag\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.OpenTag))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"CloseTag\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.CloseTag))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"NestedSlots\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeSlotPlaceholdersList(sb, slot.NestedSlots, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"NestedPlaceholders\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializePlaceholdersList(sb, slot.NestedPlaceholders, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"NestedSlottedTemplates\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeSlottedTemplatesList(sb, slot.NestedSlottedTemplates, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasNestedPlaceholders\": %t", slot.HasNestedPlaceholders))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"HasNestedSlottedTemplates\": %t", slot.HasNestedSlottedTemplates))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString(fmt.Sprintf("\"RequiresNestedProcessing\": %t", slot.RequiresNestedProcessing))
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
}

func (ar ApiResponse) SerializePlaceholdersList(sb *strings.Builder, list []model.TemplatePlaceholder, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("[")
	if indented && len(list) > 0 {
		sb.WriteString("\n")
	}
	for i, ph := range list {
		if i > 0 {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		ar.SerializePlaceholder(sb, ph, indent, indented)
	}
	if indented && len(list) > 0 {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializePlaceholder(sb *strings.Builder, ph model.TemplatePlaceholder, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Name\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.Name))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"StartIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", ph.StartIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"EndIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", ph.EndIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"FullMatch\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.FullMatch))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"TemplateKey\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.TemplateKey))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"JsonData\":")
	if indented {
		sb.WriteString(" ")
	}
	if ph.JsonData != nil {
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(fmt.Sprintf("%v", ph.JsonData)))
		sb.WriteString("\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"NestedPlaceholders\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializePlaceholdersList(sb, ph.NestedPlaceholders, indent+1, indented)
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"NestedSlots\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeSlotPlaceholdersList(sb, ph.NestedSlots, indent+1, indented)
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
}

func (ar ApiResponse) SerializeSlottedTemplatesList(sb *strings.Builder, list []model.SlottedTemplate, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("[")
	if indented && len(list) > 0 {
		sb.WriteString("\n")
	}
	for i, st := range list {
		if i > 0 {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		ar.SerializeSlottedTemplate(sb, st, indent, indented)
	}
	if indented && len(list) > 0 {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeSlottedTemplate(sb *strings.Builder, st model.SlottedTemplate, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Name\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.Name))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"StartIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", st.StartIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"EndIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", st.EndIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"FullMatch\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.FullMatch))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"TemplateKey\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.TemplateKey))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Slots\":")
	if indented {
		sb.WriteString(" ")
	}
	ar.SerializeSlotPlaceholdersList(sb, st.Slots, indent+1, indented)
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
}

func (ar ApiResponse) SerializeJsonPlaceholdersList(sb *strings.Builder, list []model.JsonPlaceholder, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("[")
	if indented && len(list) > 0 {
		sb.WriteString("\n")
	}
	for i, jp := range list {
		if i > 0 {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		ar.SerializeJsonPlaceholder(sb, jp, indent, indented)
	}
	if indented && len(list) > 0 {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeJsonPlaceholder(sb *strings.Builder, jp model.JsonPlaceholder, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Key\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Key))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Placeholder\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Placeholder))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Value\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Value))
	sb.WriteString("\"")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
}

func (ar ApiResponse) SerializeReplacementMappingsList(sb *strings.Builder, list []model.ReplacementMapping, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("[")
	if indented && len(list) > 0 {
		sb.WriteString("\n")
	}
	for i, rm := range list {
		if i > 0 {
			sb.WriteString(",")
			if indented {
				sb.WriteString("\n")
			}
		}
		if indented {
			sb.WriteString(indentStr)
			sb.WriteString("  ")
		}
		ar.SerializeReplacementMapping(sb, rm, indent, indented)
	}
	if indented && len(list) > 0 {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeReplacementMapping(sb *strings.Builder, rm model.ReplacementMapping, indent int, indented bool) {
	indentStr := strings.Repeat("  ", indent)
	sb.WriteString("{")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"StartIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", rm.StartIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"EndIndex\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", rm.EndIndex))
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"OriginalText\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.OriginalText))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"ReplacementText\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString("\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.ReplacementText))
	sb.WriteString("\"")
	sb.WriteString(",")
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
		sb.WriteString("  ")
	}
	sb.WriteString("\"Type\":")
	if indented {
		sb.WriteString(" ")
	}
	sb.WriteString(fmt.Sprintf("%d", rm.Type))
	if indented {
		sb.WriteString("\n")
		sb.WriteString(indentStr)
	}
	sb.WriteString("}")
}

// SerializePreprocessedSiteTemplates serializes PreprocessedSiteTemplates to JSON with manual formatting
func SerializePreprocessedSiteTemplates(templates *model.PreprocessedSiteTemplates, indented bool) string {
	if indented {
		return serializePreprocessedSiteTemplatesPretty(templates)
	}
	return serializePreprocessedSiteTemplatesCompact(templates)
}

// CreatePreprocessedSummary creates a summary from PreprocessedSiteTemplates (matching C# CreatePreprocessedSummary)
func CreatePreprocessedSummary(siteTemplates *model.PreprocessedSiteTemplates) *PreprocessedSummary {
	summary := &PreprocessedSummary{
		SiteName:       siteTemplates.SiteName,
		TotalTemplates: len(siteTemplates.Templates),
	}

	for _, template := range siteTemplates.Templates {
		if template.RequiresProcessing {
			summary.TemplatesRequiringProcessing++
		}
		if template.HasJsonData {
			summary.TemplatesWithJsonData++
		}
		if template.HasPlaceholders {
			summary.TemplatesWithPlaceholders++
		}
	}

	return summary
}

// SerializePreprocessedSummary serializes PreprocessedSummary to JSON with manual formatting
func SerializePreprocessedSummary(summary *PreprocessedSummary, indented bool) string {
	if indented {
		return fmt.Sprintf("{\n  \"siteName\":\"%s\",\n  \"templatesRequiringProcessing\":%d,\n  \"templatesWithJsonData\":%d,\n  \"templatesWithPlaceholders\":%d,\n  \"totalTemplates\":%d\n}",
			ApiResponse_EscapeJsonString(summary.SiteName),
			summary.TemplatesRequiringProcessing,
			summary.TemplatesWithJsonData,
			summary.TemplatesWithPlaceholders,
			summary.TotalTemplates)
	}
	return fmt.Sprintf("{\"siteName\":\"%s\",\"templatesRequiringProcessing\":%d,\"templatesWithJsonData\":%d,\"templatesWithPlaceholders\":%d,\"totalTemplates\":%d}",
		ApiResponse_EscapeJsonString(summary.SiteName),
		summary.TemplatesRequiringProcessing,
		summary.TemplatesWithJsonData,
		summary.TemplatesWithPlaceholders,
		summary.TotalTemplates)
}

// PreprocessedSummary matches the C# structure
type PreprocessedSummary struct {
	SiteName                     string
	TemplatesRequiringProcessing int
	TemplatesWithJsonData        int
	TemplatesWithPlaceholders    int
	TotalTemplates               int
}

func serializePreprocessedSiteTemplatesCompact(templates *model.PreprocessedSiteTemplates) string {
	var sb strings.Builder
	sb.WriteString("{\"siteName\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(templates.SiteName))
	sb.WriteString("\",\"templates\":{")

	first := true
	for key, template := range templates.Templates {
		if !first {
			sb.WriteString(",")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\":")
		serializePreprocessedTemplateCompact(&sb, &template)
		first = false
	}

	sb.WriteString("},\"rawTemplates\":{")
	first = true
	for key, raw := range templates.RawTemplates {
		if !first {
			sb.WriteString(",")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\":\"")
		sb.WriteString(ApiResponse_EscapeHtmlString(raw))
		sb.WriteString("\"")
		first = false
	}

	sb.WriteString("},\"templateKeys\":[")
	first = true
	for key := range templates.TemplateKeys {
		if !first {
			sb.WriteString(",")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\"")
		first = false
	}
	sb.WriteString("]}")

	return sb.String()
}

func serializePreprocessedSiteTemplatesPretty(templates *model.PreprocessedSiteTemplates) string {
	var sb strings.Builder
	sb.WriteString("{\n  \"siteName\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(templates.SiteName))
	sb.WriteString("\",\n  \"templates\":{\n")

	first := true
	for key, template := range templates.Templates {
		if !first {
			sb.WriteString(",\n")
		}
		sb.WriteString("  \"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\":{\n")
		sb.WriteString(serializePreprocessedTemplatePrettyInner(&template))
		sb.WriteString("\n}")
		first = false
	}

	sb.WriteString("\n},\n  \"rawTemplates\":{\n")
	first = true
	for key, raw := range templates.RawTemplates {
		if !first {
			sb.WriteString(",\n")
		}
		sb.WriteString("  \"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\":\"")
		sb.WriteString(ApiResponse_EscapeHtmlString(raw))
		sb.WriteString("\"")
		first = false
	}

	sb.WriteString("\n},\n  \"templateKeys\":[\n")
	first = true
	for key := range templates.TemplateKeys {
		if !first {
			sb.WriteString(",\n")
		}
		sb.WriteString("  \"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\"")
		first = false
	}
	sb.WriteString("\n]\n}")

	return sb.String()
}

func serializePreprocessedTemplateCompact(sb *strings.Builder, template *model.PreprocessedTemplate) {
	ar := ApiResponse{}
	sb.WriteString("{\"originalContent\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(template.OriginalContent))
	sb.WriteString("\",\"placeholders\":")
	ar.SerializePlaceholdersList(sb, template.Placeholders, 0, false)
	sb.WriteString(",\"slottedTemplates\":")
	ar.SerializeSlottedTemplatesList(sb, template.SlottedTemplates, 0, false)
	sb.WriteString(",\"jsonData\":")
	if template.JsonData != nil {
		sb.WriteString("\"Arshu.App.Json.JsonObject\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",\"jsonPlaceholders\":")
	ar.SerializeJsonPlaceholdersList(sb, template.JsonPlaceholders, 0, false)
	sb.WriteString(",\"replacementMappings\":")
	ar.SerializeReplacementMappingsList(sb, template.ReplacementMappings, 0, false)
	sb.WriteString(fmt.Sprintf(",\"hasPlaceholders\":%t", template.HasPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"hasSlottedTemplates\":%t", template.HasSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\"hasJsonData\":%t", template.HasJsonData))
	sb.WriteString(fmt.Sprintf(",\"hasJsonPlaceholders\":%t", template.HasJsonPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"hasReplacementMappings\":%t", template.HasReplacementMappings))
	sb.WriteString(fmt.Sprintf(",\"requiresProcessing\":%t", template.RequiresProcessing))
	sb.WriteString("}")
}

func serializePreprocessedTemplatePrettyInner(template *model.PreprocessedTemplate) string {
	var sb strings.Builder
	sb.WriteString("  \"originalContent\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(template.OriginalContent))
	sb.WriteString("\",\n  \"placeholders\":[\n")

	for i, ph := range template.Placeholders {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializePlaceholderPretty(&sb, ph)
	}
	sb.WriteString("\n]")

	sb.WriteString(",\n  \"slottedTemplates\":[\n")
	for i, st := range template.SlottedTemplates {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeSlottedTemplatePretty(&sb, st)
	}
	sb.WriteString("\n]")

	sb.WriteString(",\n  \"jsonData\":")
	if template.JsonData != nil {
		sb.WriteString("\"Arshu.App.Json.JsonObject\"")
	} else {
		sb.WriteString("null")
	}

	sb.WriteString(",\n  \"jsonPlaceholders\":[\n")
	for i, jp := range template.JsonPlaceholders {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeJsonPlaceholderPretty(&sb, jp)
	}
	sb.WriteString("\n]")

	sb.WriteString(",\n  \"replacementMappings\":[\n")
	for i, rm := range template.ReplacementMappings {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeReplacementMappingPretty(&sb, rm)
	}
	sb.WriteString("\n]")

	sb.WriteString(fmt.Sprintf(",\n  \"hasPlaceholders\":%t", template.HasPlaceholders))
	sb.WriteString(fmt.Sprintf(",\n  \"hasSlottedTemplates\":%t", template.HasSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\n  \"hasJsonData\":%t", template.HasJsonData))
	sb.WriteString(fmt.Sprintf(",\n  \"hasJsonPlaceholders\":%t", template.HasJsonPlaceholders))
	sb.WriteString(fmt.Sprintf(",\n  \"hasReplacementMappings\":%t", template.HasReplacementMappings))
	sb.WriteString(fmt.Sprintf(",\n  \"requiresProcessing\":%t", template.RequiresProcessing))

	return sb.String()
}

func serializePlaceholderPretty(sb *strings.Builder, ph model.TemplatePlaceholder) {
	sb.WriteString("{\n  \"Name\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.Name))
	sb.WriteString("\",\n  \"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", ph.StartIndex))
	sb.WriteString(",\n  \"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", ph.EndIndex))
	sb.WriteString(",\n  \"FullMatch\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.FullMatch))
	sb.WriteString("\",\n  \"TemplateKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.TemplateKey))
	sb.WriteString("\",\n  \"JsonData\":")
	if ph.JsonData != nil {
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(fmt.Sprintf("%v", ph.JsonData)))
		sb.WriteString("\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",\n  \"NestedPlaceholders\":[\n")
	for i, nph := range ph.NestedPlaceholders {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializePlaceholderPretty(sb, nph)
	}
	sb.WriteString("\n],\n  \"NestedSlots\":[\n")
	for i, ns := range ph.NestedSlots {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeSlotPlaceholderPretty(sb, ns)
	}
	sb.WriteString("\n]\n}")
}

func serializeSlottedTemplatePretty(sb *strings.Builder, st model.SlottedTemplate) {
	sb.WriteString("{\n  \"Name\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.Name))
	sb.WriteString("\",\n  \"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", st.StartIndex))
	sb.WriteString(",\n  \"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", st.EndIndex))
	sb.WriteString(",\n  \"FullMatch\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.FullMatch))
	sb.WriteString("\",\n  \"TemplateKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.TemplateKey))
	sb.WriteString("\",\n  \"Slots\":[\n")
	for i, slot := range st.Slots {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeSlotPlaceholderPretty(sb, slot)
	}
	sb.WriteString("\n]\n}")
}

func serializeSlotPlaceholderPretty(sb *strings.Builder, slot model.SlotPlaceholder) {
	sb.WriteString("{\n  \"Number\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Number))
	sb.WriteString("\",\n  \"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", slot.StartIndex))
	sb.WriteString(",\n  \"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", slot.EndIndex))
	sb.WriteString(",\n  \"Content\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Content))
	sb.WriteString("\",\n  \"SlotKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.SlotKey))
	sb.WriteString("\",\n  \"OpenTag\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.OpenTag))
	sb.WriteString("\",\n  \"CloseTag\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.CloseTag))
	sb.WriteString("\",\n  \"NestedSlots\":[\n")
	for i, ns := range slot.NestedSlots {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeSlotPlaceholderPretty(sb, ns)
	}
	sb.WriteString("\n],\n  \"NestedPlaceholders\":[\n")
	for i, np := range slot.NestedPlaceholders {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializePlaceholderPretty(sb, np)
	}
	sb.WriteString("\n],\n  \"NestedSlottedTemplates\":[\n")
	for i, nst := range slot.NestedSlottedTemplates {
		if i > 0 {
			sb.WriteString(",\n")
		}
		sb.WriteString("  ")
		serializeSlottedTemplatePretty(sb, nst)
	}
	sb.WriteString("\n]")
	sb.WriteString(fmt.Sprintf(",\n  \"HasNestedPlaceholders\":%t", slot.HasNestedPlaceholders))
	sb.WriteString(fmt.Sprintf(",\n  \"HasNestedSlottedTemplates\":%t", slot.HasNestedSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\n  \"RequiresNestedProcessing\":%t", slot.RequiresNestedProcessing))
	sb.WriteString("\n}")
}

func serializeJsonPlaceholderPretty(sb *strings.Builder, jp model.JsonPlaceholder) {
	sb.WriteString("{\n  \"Key\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Key))
	sb.WriteString("\",\n  \"Placeholder\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Placeholder))
	sb.WriteString("\",\n  \"Value\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Value))
	sb.WriteString("\"\n}")
}

func serializeReplacementMappingPretty(sb *strings.Builder, rm model.ReplacementMapping) {
	sb.WriteString("{\n  \"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", rm.StartIndex))
	sb.WriteString(",\n  \"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", rm.EndIndex))
	sb.WriteString(",\n  \"OriginalText\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.OriginalText))
	sb.WriteString("\",\n  \"ReplacementText\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.ReplacementText))
	sb.WriteString("\",\n  \"Type\":")
	sb.WriteString(fmt.Sprintf("%d", rm.Type))
	sb.WriteString("\n}")
}

// serializeJsonDataMap manually serializes a map[string]interface{} to JSON format
// This handles booleans, strings, numbers, arrays, and nested objects properly
func serializeJsonDataMap(data *map[string]interface{}) string {
	if data == nil {
		return "null"
	}

	var sb strings.Builder
	sb.WriteString("{")
	first := true

	for key, value := range *data {
		if !first {
			sb.WriteString(",")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(key))
		sb.WriteString("\":")
		sb.WriteString(serializeJsonValue(value))
		first = false
	}

	sb.WriteString("}")
	return sb.String()
}

// serializeJsonValue manually serializes any JSON value to proper JSON format
func serializeJsonValue(value interface{}) string {
	switch v := value.(type) {
	case string:
		return "\"" + ApiResponse_EscapeJsonString(v) + "\""
	case bool:
		if v {
			return "true"
		}
		return "false"
	case float64:
		return fmt.Sprintf("%g", v)
	case int:
		return fmt.Sprintf("%d", v)
	case int64:
		return fmt.Sprintf("%d", v)
	case []interface{}:
		var sb strings.Builder
		sb.WriteString("[")
		for i, item := range v {
			if i > 0 {
				sb.WriteString(",")
			}
			sb.WriteString(serializeJsonValue(item))
		}
		sb.WriteString("]")
		return sb.String()
	case map[string]interface{}:
		return serializeJsonDataMap(&v)
	case nil:
		return "null"
	default:
		// Fallback to string representation
		return "\"" + ApiResponse_EscapeJsonString(fmt.Sprintf("%v", v)) + "\""
	}
}
