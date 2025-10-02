package template_model

type PreprocessedSiteTemplates struct {
	SiteName     string                          `json:"siteName"`
	Templates    map[string]PreprocessedTemplate `json:"templates"`
	RawTemplates map[string]string               `json:"rawTemplates"`
	TemplateKeys map[string]struct{}             `json:"templateKeys"`
}

type PreprocessedTemplate struct {
	OriginalContent     string                  `json:"originalContent"`
	Placeholders        []TemplatePlaceholder   `json:"placeholders"`
	SlottedTemplates    []SlottedTemplate       `json:"slottedTemplates"`
	JsonData            *map[string]interface{} `json:"jsonData,omitempty"`
	JsonPlaceholders    []JsonPlaceholder       `json:"jsonPlaceholders"`
	ReplacementMappings []ReplacementMapping    `json:"replacementMappings"`
	HTML                string                  `json:"html,omitempty"`
	JSON                *string                 `json:"json,omitempty"`

	// Convenience boolean flags for JSON output
	HasPlaceholders        bool `json:"hasPlaceholders"`
	HasSlottedTemplates    bool `json:"hasSlottedTemplates"`
	HasJsonData            bool `json:"hasJsonData"`
	HasJsonPlaceholders    bool `json:"hasJsonPlaceholders"`
	HasReplacementMappings bool `json:"hasReplacementMappings"`
	RequiresProcessing     bool `json:"requiresProcessing"`
}

type SlottedTemplate struct {
	Name         string                  `json:"name"`
	StartIndex   int                     `json:"startIndex"`
	EndIndex     int                     `json:"endIndex"`
	FullMatch    string                  `json:"fullMatch"`
	InnerContent string                  `json:"innerContent"`
	Slots        []SlotPlaceholder       `json:"slots"`
	TemplateKey  string                  `json:"templateKey"`
	JsonData     *map[string]interface{} `json:"jsonData,omitempty"`
}

type SlotPlaceholder struct {
	NestedSlots            []SlotPlaceholder     `json:"nestedSlots"`
	Number                 string                `json:"number"`
	StartIndex             int                   `json:"startIndex"`
	EndIndex               int                   `json:"endIndex"`
	Content                string                `json:"content"`
	SlotKey                string                `json:"slotKey"`
	OpenTag                string                `json:"openTag"`
	CloseTag               string                `json:"closeTag"`
	NestedPlaceholders     []TemplatePlaceholder `json:"nestedPlaceholders"`
	NestedSlottedTemplates []SlottedTemplate     `json:"nestedSlottedTemplates"`
}

type JsonPlaceholder struct {
	Key         string `json:"key"`
	Placeholder string `json:"placeholder"`
	Value       string `json:"value"`
}

type ReplacementMapping struct {
	StartIndex      int             `json:"startIndex"`
	EndIndex        int             `json:"endIndex"`
	OriginalText    string          `json:"originalText"`
	ReplacementText string          `json:"replacementText"`
	Type            ReplacementType `json:"type"`
}

type ReplacementType int

const (
	JsonPlaceholderType ReplacementType = iota
	SimpleTemplateType
	SlottedTemplateType
)

type TemplatePlaceholder struct {
	Name               string                  `json:"name"`
	StartIndex         int                     `json:"startIndex"`
	EndIndex           int                     `json:"endIndex"`
	FullMatch          string                  `json:"fullMatch"`
	TemplateKey        string                  `json:"templateKey"`
	JsonData           *map[string]interface{} `json:"jsonData,omitempty"`
	NestedPlaceholders []TemplatePlaceholder   `json:"nestedPlaceholders"`
	NestedSlots        []SlotPlaceholder       `json:"nestedSlots"`
}

// UpdateFlags sets the convenience boolean flags based on current template state
func (pt *PreprocessedTemplate) UpdateFlags() {
	pt.HasPlaceholders = len(pt.Placeholders) > 0
	pt.HasSlottedTemplates = len(pt.SlottedTemplates) > 0
	pt.HasJsonData = pt.JsonData != nil
	pt.HasJsonPlaceholders = len(pt.JsonPlaceholders) > 0
	pt.HasReplacementMappings = len(pt.ReplacementMappings) > 0
	pt.RequiresProcessing = pt.HasPlaceholders || pt.HasSlottedTemplates || pt.HasJsonData || pt.HasJsonPlaceholders || pt.HasReplacementMappings
}
