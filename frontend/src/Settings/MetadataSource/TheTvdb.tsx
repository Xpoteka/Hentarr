import React from 'react';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import SectionHeading from 'Components/SectionHeading';
import translate from 'Utilities/String/translate';
import styles from './TheTvdb.module.css';

function TheTvdb() {
  return (
    <div className={styles.container}>
      <img
        className={styles.image}
        src={`${window.Sonarr.urlBase}/Content/Images/logo.svg`}
      />

      <div className={styles.info}>
        <SectionHeading
          title={translate('TheTvdb')}
          description={
            <InlineMarkdown
              data={translate(
                'SeriesAndEpisodeInformationIsProvidedByTheTVDB',
                {
                  url: 'https://anilist.co',
                }
              )}
            />
          }
        />
      </div>
    </div>
  );
}

export default TheTvdb;
