import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { fetchMetadataSourceSettings, saveMetadataSourceSettings, setMetadataSourceSettingsValue } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import MetadataSourceSettings from './MetadataSourceSettings';

const SECTION = 'metadataSource';

function createMapStateToProps() {
  return createSelector(
    (state) => state.settings.advancedSettings,
    createSettingsSectionSelector(SECTION),
    (advancedSettings, sectionSettings) => {
      return {
        advancedSettings,
        ...sectionSettings
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchMetadataSourceSettings: fetchMetadataSourceSettings,
  dispatchSetMetadataSourceSettingsValue: setMetadataSourceSettingsValue,
  dispatchSaveMetadataSourceSettings: saveMetadataSourceSettings,
  dispatchClearPendingChanges: clearPendingChanges
};

class MetadataSourceSettingsConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    this.props.dispatchFetchMetadataSourceSettings();
  }

  componentWillUnmount() {
    this.props.dispatchClearPendingChanges({ section: `settings.${SECTION}` });
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.dispatchSetMetadataSourceSettingsValue({ name, value });
  };

  onSavePress = () => {
    this.props.dispatchSaveMetadataSourceSettings();
  };

  //
  // Render

  render() {
    return (
      <MetadataSourceSettings
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        {...this.props}
      />
    );
  }
}

MetadataSourceSettingsConnector.propTypes = {
  dispatchFetchMetadataSourceSettings: PropTypes.func.isRequired,
  dispatchSetMetadataSourceSettingsValue: PropTypes.func.isRequired,
  dispatchSaveMetadataSourceSettings: PropTypes.func.isRequired,
  dispatchClearPendingChanges: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(MetadataSourceSettingsConnector);
