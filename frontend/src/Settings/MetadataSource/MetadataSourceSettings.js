import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import SettingsToolbarConnector from 'Settings/SettingsToolbarConnector';
import translate from 'Utilities/String/translate';
import AniDb from './AniDb';

function MetadataSourceSettings() {
  return (
    <PageContent title={translate('MetadataSourceSettings')} >
      <SettingsToolbarConnector
        showSave={false}
      />

      <PageContentBody>
        <AniDb />
      </PageContentBody>
    </PageContent>
  );
}

export default MetadataSourceSettings;
