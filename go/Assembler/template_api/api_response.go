package template_api

import (
	"assembler/template_model"
	"fmt"
	"strings"
)

type TemplateData struct {
	Html string
	Json string
}

type PreProcessTemplateMetadata struct {
	OriginalContent        string
	Placeholders           []template_model.TemplatePlaceholder
	SlottedTemplates       []template_model.SlottedTemplate
	JsonData               *map[string]interface{}
	JsonPlaceholders       []template_model.JsonPlaceholder
	ReplacementMappings    []template_model.ReplacementMapping
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

func (ar ApiResponse) SerializeToJson() string {
	var sb strings.Builder
	sb.WriteString("{")
	sb.WriteString("\"Templates\":")
	sb.WriteString(ar.serializeTemplates())
	sb.WriteString(",\"PreProcessTemplates\":")
	sb.WriteString(ar.serializePreProcessTemplates())
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

func (ar ApiResponse) serializeTemplates() string {
	var sb strings.Builder
	sb.WriteString("{")
	first := true
	for k, v := range ar.Templates {
		if !first {
			sb.WriteString(",")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(k))
		sb.WriteString("\":")
		sb.WriteString(ar.serializeTemplateData(v))
		first = false
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) serializeTemplateData(td TemplateData) string {
	var sb strings.Builder
	sb.WriteString("{\"Html\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(td.Html))
	sb.WriteString("\",\"Json\":")
	if td.Json != "" {
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(td.Json))
		sb.WriteString("\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) serializePreProcessTemplates() string {
	var sb strings.Builder
	sb.WriteString("{")
	first := true
	for k, v := range ar.PreProcessTemplates {
		if !first {
			sb.WriteString(",")
		}
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(k))
		sb.WriteString("\":")
		sb.WriteString(ar.serializePreProcessTemplateMetadata(v))
		first = false
	}
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) serializePreProcessTemplateMetadata(pm PreProcessTemplateMetadata) string {
	var sb strings.Builder
	sb.WriteString("{\"OriginalContent\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(pm.OriginalContent))
	sb.WriteString("\",\"Placeholders\":")
	ar.SerializePlaceholdersList(&sb, pm.Placeholders)
	sb.WriteString(",\"SlottedTemplates\":")
	ar.SerializeSlottedTemplatesList(&sb, pm.SlottedTemplates)
	sb.WriteString(",\"JsonData\":")
	if pm.JsonData != nil {
		// JsonData should be serialized as proper JSON, not as a string
		// Check if it's already JSON format or needs to be treated as raw JSON
		jsonStr := fmt.Sprintf("%v", *pm.JsonData)
		if jsonStr == "map[]" || jsonStr == "<nil>" {
			sb.WriteString("null")
		} else if (jsonStr[0] == '{' && jsonStr[len(jsonStr)-1] == '}') || (jsonStr[0] == '[' && jsonStr[len(jsonStr)-1] == ']') {
			// Appears to be JSON already, include as-is
			sb.WriteString(jsonStr)
		} else {
			// Treat as string value
			sb.WriteString("\"")
			sb.WriteString(ApiResponse_EscapeJsonString(jsonStr))
			sb.WriteString("\"")
		}
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",\"JsonPlaceholders\":")
	ar.SerializeJsonPlaceholdersList(&sb, pm.JsonPlaceholders)
	sb.WriteString(",\"ReplacementMappings\":")
	ar.SerializeReplacementMappingsList(&sb, pm.ReplacementMappings)
	sb.WriteString(fmt.Sprintf(",\"HasPlaceholders\":%t", pm.HasPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"HasSlottedTemplates\":%t", pm.HasSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\"HasJsonData\":%t", pm.HasJsonData))
	sb.WriteString(fmt.Sprintf(",\"HasJsonPlaceholders\":%t", pm.HasJsonPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"HasReplacementMappings\":%t", pm.HasReplacementMappings))
	sb.WriteString(fmt.Sprintf(",\"RequiresProcessing\":%t", pm.RequiresProcessing))
	sb.WriteString("}")
	return sb.String()
}

func (ar ApiResponse) SerializeSlotPlaceholdersList(sb *strings.Builder, list []template_model.SlotPlaceholder) {
	sb.WriteString("[")
	for i, slot := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		ar.SerializeSlotPlaceholder(sb, slot)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeSlotPlaceholder(sb *strings.Builder, slot template_model.SlotPlaceholder) {
	sb.WriteString("{")
	sb.WriteString("\"Number\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Number))
	sb.WriteString("\",\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", slot.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", slot.EndIndex))
	sb.WriteString(",\"Content\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Content))
	sb.WriteString("\",\"SlotKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.SlotKey))
	sb.WriteString("\",\"OpenTag\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.OpenTag))
	sb.WriteString("\",\"CloseTag\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.CloseTag))
	sb.WriteString("\",\"NestedSlots\":")
	ar.SerializeSlotPlaceholdersList(sb, slot.NestedSlots)
	sb.WriteString(",\"NestedPlaceholders\":")
	ar.SerializePlaceholdersList(sb, slot.NestedPlaceholders)
	sb.WriteString(",\"NestedSlottedTemplates\":")
	ar.SerializeSlottedTemplatesList(sb, slot.NestedSlottedTemplates)
	sb.WriteString(fmt.Sprintf(",\"HasNestedPlaceholders\":%t", slot.HasNestedPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"HasNestedSlottedTemplates\":%t", slot.HasNestedSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\"RequiresNestedProcessing\":%t", slot.RequiresNestedProcessing))
	sb.WriteString("}")
}

func (ar ApiResponse) SerializePlaceholdersList(sb *strings.Builder, list []template_model.TemplatePlaceholder) {
	sb.WriteString("[")
	for i, ph := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		ar.SerializePlaceholder(sb, ph)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializePlaceholder(sb *strings.Builder, ph template_model.TemplatePlaceholder) {
	sb.WriteString("{")
	sb.WriteString("\"Name\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.Name))
	sb.WriteString("\",\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", ph.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", ph.EndIndex))
	sb.WriteString(",\"FullMatch\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.FullMatch))
	sb.WriteString("\",\"TemplateKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.TemplateKey))
	sb.WriteString("\",\"JsonData\":")
	if ph.JsonData != nil {
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(fmt.Sprintf("%v", ph.JsonData)))
		sb.WriteString("\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",\"NestedPlaceholders\":")
	ar.SerializePlaceholdersList(sb, ph.NestedPlaceholders)
	sb.WriteString(",\"NestedSlots\":")
	ar.SerializeSlotPlaceholdersList(sb, ph.NestedSlots)
	sb.WriteString("}")
}

func (ar ApiResponse) SerializeSlottedTemplatesList(sb *strings.Builder, list []template_model.SlottedTemplate) {
	sb.WriteString("[")
	for i, st := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		ar.SerializeSlottedTemplate(sb, st)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeSlottedTemplate(sb *strings.Builder, st template_model.SlottedTemplate) {
	sb.WriteString("{")
	sb.WriteString("\"Name\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.Name))
	sb.WriteString("\",\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", st.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", st.EndIndex))
	sb.WriteString(",\"FullMatch\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.FullMatch))
	sb.WriteString("\",\"TemplateKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.TemplateKey))
	sb.WriteString("\",\"Slots\":")
	ar.SerializeSlotPlaceholdersList(sb, st.Slots)
	sb.WriteString("}")
}

func (ar ApiResponse) SerializeJsonPlaceholdersList(sb *strings.Builder, list []template_model.JsonPlaceholder) {
	sb.WriteString("[")
	for i, jp := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		ar.SerializeJsonPlaceholder(sb, jp)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeJsonPlaceholder(sb *strings.Builder, jp template_model.JsonPlaceholder) {
	sb.WriteString("{")
	sb.WriteString("\"Key\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Key))
	sb.WriteString("\",\"Placeholder\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Placeholder))
	sb.WriteString("\",\"Value\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Value))
	sb.WriteString("\"}")
}

func (ar ApiResponse) SerializeReplacementMappingsList(sb *strings.Builder, list []template_model.ReplacementMapping) {
	sb.WriteString("[")
	for i, rm := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		ar.SerializeReplacementMapping(sb, rm)
	}
	sb.WriteString("]")
}

func (ar ApiResponse) SerializeReplacementMapping(sb *strings.Builder, rm template_model.ReplacementMapping) {
	sb.WriteString("{")
	sb.WriteString("\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", rm.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", rm.EndIndex))
	sb.WriteString(",\"OriginalText\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.OriginalText))
	sb.WriteString("\",\"ReplacementText\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.ReplacementText))
	sb.WriteString("\",\"Type\":")
	sb.WriteString(fmt.Sprintf("%d", rm.Type))
	sb.WriteString("}")
}

func SerializeSlotPlaceholdersList(sb *strings.Builder, list []template_model.SlotPlaceholder) {
	sb.WriteString("[")
	for i, slot := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		SerializeSlotPlaceholder(sb, slot)
	}
	sb.WriteString("]")
}

func SerializeSlotPlaceholder(sb *strings.Builder, slot template_model.SlotPlaceholder) {
	sb.WriteString("{")
	sb.WriteString("\"Number\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Number))
	sb.WriteString("\",\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", slot.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", slot.EndIndex))
	sb.WriteString(",\"Content\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.Content))
	sb.WriteString("\",\"SlotKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.SlotKey))
	sb.WriteString("\",\"OpenTag\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.OpenTag))
	sb.WriteString("\",\"CloseTag\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(slot.CloseTag))
	sb.WriteString("\",\"NestedSlots\":")
	SerializeSlotPlaceholdersList(sb, slot.NestedSlots)
	sb.WriteString(",\"NestedPlaceholders\":")
	SerializePlaceholdersList(sb, slot.NestedPlaceholders)
	sb.WriteString(",\"NestedSlottedTemplates\":")
	SerializeSlottedTemplatesList(sb, slot.NestedSlottedTemplates)
	sb.WriteString(fmt.Sprintf(",\"HasNestedPlaceholders\":%t", slot.HasNestedPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"HasNestedSlottedTemplates\":%t", slot.HasNestedSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\"RequiresNestedProcessing\":%t", slot.RequiresNestedProcessing))
	sb.WriteString("}")
}

func SerializePlaceholdersList(sb *strings.Builder, list []template_model.TemplatePlaceholder) {
	sb.WriteString("[")
	for i, ph := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		SerializePlaceholder(sb, ph)
	}
	sb.WriteString("]")
}

func SerializePlaceholder(sb *strings.Builder, ph template_model.TemplatePlaceholder) {
	sb.WriteString("{")
	sb.WriteString("\"Name\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.Name))
	sb.WriteString("\",\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", ph.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", ph.EndIndex))
	sb.WriteString(",\"FullMatch\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.FullMatch))
	sb.WriteString("\",\"TemplateKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(ph.TemplateKey))
	sb.WriteString("\",\"JsonData\":")
	if ph.JsonData != nil {
		sb.WriteString("\"")
		sb.WriteString(ApiResponse_EscapeJsonString(fmt.Sprintf("%v", ph.JsonData)))
		sb.WriteString("\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",\"NestedPlaceholders\":")
	SerializePlaceholdersList(sb, ph.NestedPlaceholders)
	sb.WriteString(",\"NestedSlots\":")
	SerializeSlotPlaceholdersList(sb, ph.NestedSlots)
	sb.WriteString("}")
}

func SerializeSlottedTemplatesList(sb *strings.Builder, list []template_model.SlottedTemplate) {
	sb.WriteString("[")
	for i, st := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		SerializeSlottedTemplate(sb, st)
	}
	sb.WriteString("]")
}

func SerializeSlottedTemplate(sb *strings.Builder, st template_model.SlottedTemplate) {
	sb.WriteString("{")
	sb.WriteString("\"Name\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.Name))
	sb.WriteString("\",\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", st.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", st.EndIndex))
	sb.WriteString(",\"FullMatch\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.FullMatch))
	sb.WriteString("\",\"TemplateKey\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(st.TemplateKey))
	sb.WriteString("\",\"Slots\":")
	SerializeSlotPlaceholdersList(sb, st.Slots)
	sb.WriteString("}")
}

func SerializeJsonPlaceholdersList(sb *strings.Builder, list []template_model.JsonPlaceholder) {
	sb.WriteString("[")
	for i, jp := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		SerializeJsonPlaceholder(sb, jp)
	}
	sb.WriteString("]")
}

func SerializeJsonPlaceholder(sb *strings.Builder, jp template_model.JsonPlaceholder) {
	sb.WriteString("{")
	sb.WriteString("\"Key\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Key))
	sb.WriteString("\",\"Placeholder\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Placeholder))
	sb.WriteString("\",\"Value\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Value))
	sb.WriteString("\"}")
}

func SerializeReplacementMappingsList(sb *strings.Builder, list []template_model.ReplacementMapping) {
	sb.WriteString("[")
	for i, rm := range list {
		if i > 0 {
			sb.WriteString(",")
		}
		SerializeReplacementMapping(sb, rm)
	}
	sb.WriteString("]")
}

func SerializeReplacementMapping(sb *strings.Builder, rm template_model.ReplacementMapping) {
	sb.WriteString("{")
	sb.WriteString("\"StartIndex\":")
	sb.WriteString(fmt.Sprintf("%d", rm.StartIndex))
	sb.WriteString(",\"EndIndex\":")
	sb.WriteString(fmt.Sprintf("%d", rm.EndIndex))
	sb.WriteString(",\"OriginalText\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.OriginalText))
	sb.WriteString("\",\"ReplacementText\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(rm.ReplacementText))
	sb.WriteString("\",\"Type\":")
	sb.WriteString(fmt.Sprintf("%d", rm.Type))
	sb.WriteString("}")
}

// SerializePreprocessedSiteTemplates serializes PreprocessedSiteTemplates to JSON with manual formatting
func SerializePreprocessedSiteTemplates(templates *template_model.PreprocessedSiteTemplates, indented bool) string {
	if indented {
		return serializePreprocessedSiteTemplatesPretty(templates)
	}
	return serializePreprocessedSiteTemplatesCompact(templates)
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

func serializePreprocessedSiteTemplatesCompact(templates *template_model.PreprocessedSiteTemplates) string {
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

func serializePreprocessedSiteTemplatesPretty(templates *template_model.PreprocessedSiteTemplates) string {
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

func serializePreprocessedTemplateCompact(sb *strings.Builder, template *template_model.PreprocessedTemplate) {
	sb.WriteString("{\"originalContent\":\"")
	sb.WriteString(ApiResponse_EscapeHtmlString(template.OriginalContent))
	sb.WriteString("\",\"placeholders\":")
	SerializePlaceholdersList(sb, template.Placeholders)
	sb.WriteString(",\"slottedTemplates\":")
	SerializeSlottedTemplatesList(sb, template.SlottedTemplates)
	sb.WriteString(",\"jsonData\":")
	if template.JsonData != nil {
		sb.WriteString("\"Arshu.App.Json.JsonObject\"")
	} else {
		sb.WriteString("null")
	}
	sb.WriteString(",\"jsonPlaceholders\":")
	SerializeJsonPlaceholdersList(sb, template.JsonPlaceholders)
	sb.WriteString(",\"replacementMappings\":")
	SerializeReplacementMappingsList(sb, template.ReplacementMappings)
	sb.WriteString(fmt.Sprintf(",\"hasPlaceholders\":%t", template.HasPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"hasSlottedTemplates\":%t", template.HasSlottedTemplates))
	sb.WriteString(fmt.Sprintf(",\"hasJsonData\":%t", template.HasJsonData))
	sb.WriteString(fmt.Sprintf(",\"hasJsonPlaceholders\":%t", template.HasJsonPlaceholders))
	sb.WriteString(fmt.Sprintf(",\"hasReplacementMappings\":%t", template.HasReplacementMappings))
	sb.WriteString(fmt.Sprintf(",\"requiresProcessing\":%t", template.RequiresProcessing))
	sb.WriteString("}")
}

func serializePreprocessedTemplatePrettyInner(template *template_model.PreprocessedTemplate) string {
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

func serializePlaceholderPretty(sb *strings.Builder, ph template_model.TemplatePlaceholder) {
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

func serializeSlottedTemplatePretty(sb *strings.Builder, st template_model.SlottedTemplate) {
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

func serializeSlotPlaceholderPretty(sb *strings.Builder, slot template_model.SlotPlaceholder) {
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

func serializeJsonPlaceholderPretty(sb *strings.Builder, jp template_model.JsonPlaceholder) {
	sb.WriteString("{\n  \"Key\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Key))
	sb.WriteString("\",\n  \"Placeholder\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Placeholder))
	sb.WriteString("\",\n  \"Value\":\"")
	sb.WriteString(ApiResponse_EscapeJsonString(jp.Value))
	sb.WriteString("\"\n}")
}

func serializeReplacementMappingPretty(sb *strings.Builder, rm template_model.ReplacementMapping) {
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
