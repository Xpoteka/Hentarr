import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { inputTypes, kinds } from 'Helpers/Props';
import SettingsToolbarConnector from 'Settings/SettingsToolbarConnector';
import translate from 'Utilities/String/translate';
import AniDb from './AniDb';

class MetadataSourceSettings extends Component {

  //
  // Render

  render() {
    const {
      advancedSettings,
      isFetching,
      error,
      settings,
      hasSettings,
      onInputChange,
      onSavePress,
      ...otherProps
    } = this.props;

    return (
      <PageContent title={translate('MetadataSourceSettings')}>
        <SettingsToolbarConnector
          {...otherProps}
          onSavePress={onSavePress}
        />

        <PageContentBody>
          <AniDb />

          {
            isFetching ?
              <LoadingIndicator /> :
              null
          }

          {
            !isFetching && error ?
              <Alert kind={kinds.DANGER}>
                {translate('MetadataSourceSettingsLoadError')}
              </Alert> :
              null
          }

          {
            hasSettings && !isFetching && !error ?
              <Form
                id="metadataSourceSettings"
                {...otherProps}
              >
                <FieldSet legend={translate('AniDbLogin')}>
                  <FormGroup>
                    <FormLabel>{translate('AniDbClientName')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="aniDbClientName"
                      helpText={translate('AniDbClientNameHelpText')}
                      helpLink="https://anidb.net/software/add"
                      onChange={onInputChange}
                      {...settings.aniDbClientName}
                    />
                  </FormGroup>

                  <FormGroup
                    advancedSettings={advancedSettings}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('AniDbClientVersion')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.NUMBER}
                      name="aniDbClientVersion"
                      min={1}
                      helpText={translate('AniDbClientVersionHelpText')}
                      onChange={onInputChange}
                      {...settings.aniDbClientVersion}
                    />
                  </FormGroup>
                </FieldSet>
              </Form> :
              null
          }
        </PageContentBody>
      </PageContent>
    );
  }

}

MetadataSourceSettings.propTypes = {
  advancedSettings: PropTypes.bool.isRequired,
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  settings: PropTypes.object.isRequired,
  hasSettings: PropTypes.bool.isRequired,
  onSavePress: PropTypes.func.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default MetadataSourceSettings;
