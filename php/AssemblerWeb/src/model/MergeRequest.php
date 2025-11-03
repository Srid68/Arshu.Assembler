<?php
class MergeRequest {
    public ?string $appSite;
    public ?string $appView;
    public ?string $engineType;

    public function __construct(?string $appSite, ?string $appView, ?string $engineType) {
        $this->appSite = $appSite;
        $this->appView = $appView;
        $this->engineType = $engineType;
    }
}
?>