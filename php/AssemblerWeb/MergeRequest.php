<?php
class MergeRequest {
    public ?string $appSite;
    public ?string $appView;
    public ?string $appViewPrefix;
    public ?string $appFile;
    public ?string $engineType;

    public function __construct(?string $appSite, ?string $appView, ?string $appViewPrefix, ?string $appFile, ?string $engineType) {
        $this->appSite = $appSite;
        $this->appView = $appView;
        $this->appViewPrefix = $appViewPrefix;
        $this->appFile = $appFile;
        $this->engineType = $engineType;
    }
}
?>