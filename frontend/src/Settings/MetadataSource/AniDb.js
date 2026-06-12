import React from 'react';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import translate from 'Utilities/String/translate';
import styles from './AniDb.css';

function AniDb(props) {
  return (
    <div className={styles.container}>
      <div className={styles.info}>
        <div className={styles.title}>
          {translate('AniDb')}
        </div>

        <InlineMarkdown data={translate('SiteAndEpisodeInformationIsProvidedByAniDb')} />
      </div>

    </div>
  );
}

export default AniDb;
