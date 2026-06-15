import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createSaveHandler from 'Store/Actions/Creators/createSaveHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';

//
// Variables

const section = 'settings.metadataSource';

//
// Actions Types

export const FETCH_METADATA_SOURCE_SETTINGS = 'settings/metadataSource/fetchMetadataSourceSettings';
export const SAVE_METADATA_SOURCE_SETTINGS = 'settings/metadataSource/saveMetadataSourceSettings';
export const SET_METADATA_SOURCE_SETTINGS_VALUE = 'settings/metadataSource/setMetadataSourceSettingsValue';

//
// Action Creators

export const fetchMetadataSourceSettings = createThunk(FETCH_METADATA_SOURCE_SETTINGS);
export const saveMetadataSourceSettings = createThunk(SAVE_METADATA_SOURCE_SETTINGS);
export const setMetadataSourceSettingsValue = createAction(SET_METADATA_SOURCE_SETTINGS_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    pendingChanges: {},
    isSaving: false,
    saveError: null,
    item: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_METADATA_SOURCE_SETTINGS]: createFetchHandler(section, '/config/metadatasource'),
    [SAVE_METADATA_SOURCE_SETTINGS]: createSaveHandler(section, '/config/metadatasource')
  },

  //
  // Reducers

  reducers: {
    [SET_METADATA_SOURCE_SETTINGS_VALUE]: createSetSettingValueReducer(section)
  }

};
